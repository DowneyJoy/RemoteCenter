using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace RemoteCenter
{
    /// 客户端：全屏循环播放视频，或轮播服务端推送的图片；新媒体接收完成后自动切换并持久记录。
    public class ClientApp : MonoBehaviour
    {
        [Serializable]
        class PlaylistData
        {
            public string mode;       // "video" 或 "images"
            public string videoFile;  // 旧版单视频字段（读取兼容）
            public List<string> videoFiles;
            public List<string> imageFiles;
            public float interval;
        }

        readonly ConcurrentQueue<Action> _mainQueue = new ConcurrentQueue<Action>();

        VideoClient _client;
        InitCommandListener _initListener;
        VideoPlayer _player;
        AudioSource _audio;
        RenderTexture _renderTexture;
        Texture2D _imageTexture;
        RawImage _rawImage;
        AspectRatioFitter _fitter;

        Text _status;
        InputField _ipInput;
        GameObject _statusPanel;
        GameObject _receivePanel;
        Text _receiveTitle;
        Text _receivePercent;
        Slider _receiveSlider;

        // 幻灯片状态（仅主线程访问）
        List<string> _slideshowPaths;
        int _slideshowIndex;
        float _slideshowInterval = 5f;
        float _slideshowTimer;
        bool _slideshowActive;

        // 视频列表状态（仅主线程访问）：单个视频循环播放，多个视频顺序轮播
        List<string> _videoPaths;
        int _videoIndex;
        bool _videoPlaylistActive;
        bool _preparingVideo;
        
        volatile string _host = "127.0.0.1";
        string _netState = "未连接";
        string _playState = "-";
        UDPReceiver _udpReceiver;
        string PlaylistPath
        {
            get { return _client != null ? Path.Combine(_client.SaveDirectory, "playlist.json") : null; }
        }
        bool isPrepared = false;
        // 当前音量（用于记录，方便增减）
        float currentVolume = 1;
        float volumeStep = 0.1f;
        void Start()
        {
            Loom.RunAsync(() => { });
            // 循环播放场景保持屏幕常亮（Android 息屏会中断播放）
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }

            _imageTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "SlideshowImage" };
            BuildUI();
            CreateVideoPlayer();

            _client = new VideoClient(() => _host, VideoTransferProtocol.Port);
            _client.StateChanged += s => Post(() => { _netState = s; RefreshStatus(); });
            _client.ReceivingStarted += (name, size) => Post(() =>
            {
                _receivePanel.SetActive(true);
                _receiveTitle.text = $"正在接收: {name}（{size / 1048576.0:0.00} MB）";
                _receiveSlider.value = 0f;
                _receivePercent.text = "0%";
            });
            _client.ReceivingProgress += (received, total) => Post(() =>
            {
                _receiveSlider.value = total > 0 ? (float)((double)received / total) : 0f;
                _receivePercent.text = total > 0 ? $"{received * 100 / total}%" : "0%";
            });
            _client.VideosReceived += paths => Post(() =>
            {
                isPrepared = false;
                _receivePanel.SetActive(false);
                StartVideoPlaylist(paths);
                SavePlaylistVideos(paths);
                CleanupUnreferencedMedia(paths);
            });
            _client.ImagesReceived += (paths, interval) => Post(() =>
            {
                isPrepared = false;
                _receivePanel.SetActive(false);
                StartSlideshow(paths, interval);
                SavePlaylistImages(paths, interval);
                CleanupUnreferencedMedia(paths);
            });
            _client.Error += m => Post(() =>
            {
                _receivePanel.SetActive(false);
                _netState = "错误: " + m;
                RefreshStatus();
            });
            _client.ServerDisconnect += () => Post(() =>
            {
                _statusPanel.SetActive(false);
                Debug.Log("[ClientApp] 服务端请求断开，已停止连接");
            });
            
            // 启动时读取上次保存的服务端 IP（初始化命令持久化的结果）
            string storedIp = LoadStoredServerIp();
            if (!string.IsNullOrEmpty(storedIp))
            {
                _host = storedIp;
                _ipInput.text = storedIp;
                Debug.Log($"[ClientApp] 使用已保存的服务端 IP: {storedIp}");
            }

            // 初始化命令监听：服务端通过 UDP 下发自己的 IP
            _initListener = new InitCommandListener(VideoTransferProtocol.InitPort);
            _initListener.ServerInfoReceived += (ip, port) => Post(() => HandleInitCommand(ip, port));
            _initListener.Error += m => Post(() =>
            {
                _netState = m;
                RefreshStatus();
                Debug.LogWarning("[ClientApp] " + m);
            });
            _initListener.Start();

            PlayStartupMedia();
            EventDispatcher.Instance.AddListener("replay",RePlayVideoEvent);
            EventDispatcher.Instance.AddListener("play",PlayVideoEvent);
            EventDispatcher.Instance.AddListener("pause",PauseVideoEvent);
            EventDispatcher.Instance.AddListener("mute",MuteAudioEvent);
            EventDispatcher.Instance.AddListener("cancelmute",CancelMuteAudioEvent);
            EventDispatcher.Instance.AddListener("volumeadd",VolumeAddEvent);
            EventDispatcher.Instance.AddListener("volumeminus",VolumeMinus);
        }

        private void VolumeMinus()
        {
            VolumeDown();
        }

        private void VolumeAddEvent()
        {
            VolumeUp();
        }

        private void CancelMuteAudioEvent()
        {
            SetMute(false);
        }

        private void MuteAudioEvent()
        {
            SetMute(true);
        }

        private void PauseVideoEvent()
        {
            Pause();
        }

        private void PlayVideoEvent()
        {
            Play();
        }

        private void RePlayVideoEvent()
        {
            Restart();
        }

        void Update()
        {
            while (_mainQueue.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }

            // 图片轮播计时
            if (_slideshowActive && _slideshowPaths != null && _slideshowPaths.Count > 1)
            {
                _slideshowTimer += Time.unscaledDeltaTime;
                if (_slideshowTimer >= _slideshowInterval)
                {
                    _slideshowTimer = 0f;
                    _slideshowIndex = (_slideshowIndex + 1) % _slideshowPaths.Count;
                    ShowSlideshowImage();
                }
            }
            
            // 视频列表轮播兜底：个别平台收不到 loopPointReached 事件时按播放状态推进
            if (_videoPlaylistActive && !_slideshowActive && !_preparingVideo
                && _videoPaths != null && _videoPaths.Count > 1
                && _player != null && _player.isPrepared && !_player.isPlaying)
            {
                AdvanceVideo();
            }
            
            // 视频控制
            if (_player.url != null)
            {
                if (Input.GetKeyDown(KeyCode.P))
                    Pause();
                if (Input.GetKeyDown(KeyCode.S))
                    Play();
                // 空格：播放/暂停
                if (Input.GetKeyDown(KeyCode.Space))
                    TogglePlayPause();
                if (Input.GetKeyDown(KeyCode.M))
                    SetMute(true);
                if (Input.GetKeyDown(KeyCode.N))
                    SetMute(false);
                // R：重播
                if (Input.GetKeyDown(KeyCode.R))
                    Restart();

                // + / -：音量增减
                if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
                    VolumeUp();
                if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
                    VolumeDown();
            }

        }
        
        /// 切换到视频列表中的下一个（环形）。
        void AdvanceVideo()
        {
            if (_videoPaths == null || _videoPaths.Count < 2) return;
            _videoIndex = (_videoIndex + 1) % _videoPaths.Count;
            PlayCurrentVideo();
        }

        void OnDestroy()
        {
            _client?.Dispose();
            _initListener?.Dispose();
            if (_renderTexture != null)
            {
                if (_player != null) _player.targetTexture = null;
                _renderTexture.Release();
                _renderTexture = null;
            }
            if (_imageTexture != null)
            {
                Destroy(_imageTexture);
                _imageTexture = null;
            }
            EventDispatcher.Instance.RemoveEvent("replay");
            EventDispatcher.Instance.RemoveEvent("play");
            EventDispatcher.Instance.RemoveEvent("pause");
            EventDispatcher.Instance.RemoveEvent("mute");
            EventDispatcher.Instance.RemoveEvent("cancelmute");
            EventDispatcher.Instance.RemoveEvent("volumeadd");
            EventDispatcher.Instance.RemoveEvent("volumeminus");
        }

        void Post(Action action) => _mainQueue.Enqueue(action);

        void RefreshStatus()
        {
            if (_status != null) _status.text = $"连接: {_netState}  |  播放: {_playState}";
        }
        
        /// 收到服务端初始化命令：已连接则跳过；未连接则显示状态栏、保存 IP 并启动连接。
        void HandleInitCommand(string serverIp, int port)
        {
            if (string.IsNullOrEmpty(serverIp) || !System.Net.IPAddress.TryParse(serverIp, out _)) return;
            
            if (_client.IsConnected)
            {
                _netState = $"已连接服务端，忽略初始化命令（{serverIp}）";
                RefreshStatus();
                return;
            }
            
            _host = serverIp;
            if (_ipInput != null) _ipInput.text = serverIp;
            SaveStoredServerIp(serverIp);
            
            if (_statusPanel != null) _statusPanel.SetActive(true);
            _netState = $"收到初始化命令，服务端 IP: {serverIp}，正在连接...";
            RefreshStatus();
            
            if (!_client.IsRunning) _client.Start(); // 首次或断开命令后重新激活连接循环
            _client.Wake();
        }

        string StoredServerIpPath
        {
            get { return _client != null ? Path.Combine(_client.SaveDirectory, "server_ip.txt") : null; }
        }

        void SaveStoredServerIp(string ip)
        {
            try
            {
                string path = StoredServerIpPath;
                if (string.IsNullOrEmpty(path)) return;
                Directory.CreateDirectory(_client.SaveDirectory);
                File.WriteAllText(path, ip);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ClientApp] 保存服务端 IP 失败: " + ex.Message);
            }
        }
        string LoadStoredServerIp()
        {
            try
            {
                string path = StoredServerIpPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                string ip = File.ReadAllText(path).Trim();
                return System.Net.IPAddress.TryParse(ip, out _) ? ip : null;
            }
            catch { return null; }
        }
        
        void BuildUI()
        {
            var root = UIFactory.CreateCanvas("ClientCanvas").transform;

            var udpR = new GameObject("UDP");
            _udpReceiver = udpR.AddComponent<UDPReceiver>();
            
            var videoGo = new GameObject("Video", typeof(RawImage));
            videoGo.transform.SetParent(root, false);
            _rawImage = videoGo.GetComponent<RawImage>();
            _renderTexture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32) { name = "VideoRT" };
            _renderTexture.Create();
            _rawImage.texture = _renderTexture;
            UIFactory.Stretch(_rawImage.rectTransform);
            _fitter = videoGo.AddComponent<AspectRatioFitter>();
            _fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _fitter.aspectRatio = 16f / 9f;

            // 状态栏：StatusBg / IpLabel / IpInput 统一挂在 StatusPanel 下，默认隐藏（收到初始化命令后显示）
            _statusPanel = new GameObject("StatusPanel", typeof(Image));
            _statusPanel.transform.SetParent(root, false);
            var statusPanelBg = _statusPanel.GetComponent<Image>();
            statusPanelBg.color = new Color(0f, 0f, 0f, 0.45f);
            var panelRt = _statusPanel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0f, 1f);
            panelRt.anchorMax = new Vector2(1f, 1f);
            panelRt.pivot = new Vector2(0.5f, 1f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta = new Vector2(0f, 64f);
            
            _status = UIFactory.CreateText(_statusPanel.transform, "Status", "", 20, Color.white, TextAnchor.MiddleLeft);
            var statusRt = _status.rectTransform;
            statusRt.anchorMin = new Vector2(0f, 0f);
            statusRt.anchorMax = new Vector2(0.62f, 1f);
            statusRt.offsetMin = new Vector2(14, 4);
            statusRt.offsetMax = new Vector2(-10, -4);
            
            var ipLabel = UIFactory.CreateText(_statusPanel.transform, "IpLabel", "服务端 IP:", 20, Color.white, TextAnchor.MiddleRight);
            var ipLabelRt = ipLabel.rectTransform;
            ipLabelRt.anchorMin = new Vector2(0.62f, 0f);
            ipLabelRt.anchorMax = new Vector2(0.76f, 1f);
            ipLabelRt.offsetMin = new Vector2(0, 0);
            ipLabelRt.offsetMax = new Vector2(-4, 0);
            
            _ipInput = UIFactory.CreateInputField(_statusPanel.transform, "IpInput", "127.0.0.1", "服务端 IP");
            var ipInputRt = (RectTransform)_ipInput.transform;
            ipInputRt.anchorMin = new Vector2(0.76f, 0.5f);
            ipInputRt.anchorMax = new Vector2(1f, 0.5f);
            ipInputRt.pivot = new Vector2(0.5f, 0.5f);
            ipInputRt.offsetMin = new Vector2(6, -20);
            ipInputRt.offsetMax = new Vector2(-12, 20);
            _ipInput.onEndEdit.AddListener(value =>
            {
                value = value.Trim();
                if (value.Length > 0) _host = value;
            });
            
            _receivePanel = new GameObject("ReceivePanel", typeof(Image));
            _receivePanel.transform.SetParent(root, false);
            var panelImage = _receivePanel.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.78f);
            UIFactory.Place(panelImage.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 48), new Vector2(660, 124));

            _receiveTitle = UIFactory.CreateText(_receivePanel.transform, "Title", "正在接收视频...", 20, Color.white, TextAnchor.MiddleLeft);
            UIFactory.Stretch(_receiveTitle.rectTransform);
            _receiveTitle.rectTransform.offsetMin = new Vector2(16, 62);
            _receiveTitle.rectTransform.offsetMax = new Vector2(-16, -8);

            _receiveSlider = UIFactory.CreateSlider(_receivePanel.transform, "Bar");
            UIFactory.Place((RectTransform)_receiveSlider.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(600, 16));

            _receivePercent = UIFactory.CreateText(_receivePanel.transform, "Percent", "0%", 18, Color.white, TextAnchor.MiddleLeft);
            UIFactory.Place(_receivePercent.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 10), new Vector2(300, 26));

            _receivePanel.SetActive(false);
            _statusPanel.SetActive(false);
            RefreshStatus();
        }

        void CreateVideoPlayer()
        {
            var audioGo = new GameObject("VideoAudio", typeof(AudioSource));
            _audio = audioGo.GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;

            var playerGo = new GameObject("VideoPlayer", typeof(VideoPlayer));
            _player = playerGo.GetComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.targetTexture = _renderTexture;
            _player.isLooping = true;
            _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _player.SetTargetAudioSource(0, _audio);
            _player.prepareCompleted += OnPrepareCompleted;
            _player.errorReceived += OnVideoError;
            _player.loopPointReached += OnLoopPointReached;
        }

        void OnPrepareCompleted(VideoPlayer source)
        {
            _preparingVideo = false;
            // 已切换到幻灯片模式时不恢复视频播放
            if (!_slideshowActive) source.Play();
            isPrepared = true;
        }

        void OnVideoError(VideoPlayer source, string message)
        {
            _preparingVideo = false;
            _playState = "播放错误";
            RefreshStatus();
            Debug.LogWarning("[ClientApp] 视频播放错误: " + message);
        }
        
        void OnLoopPointReached(VideoPlayer source)
        {
            // 多视频列表：当前视频播完切下一个（环形）；单视频由 isLooping 自然循环
            if (_preparingVideo || _slideshowActive || !_videoPlaylistActive) return;
            if (_videoPaths == null || _videoPaths.Count < 2) return;
            AdvanceVideo();
        }
        
        void PlayStartupMedia()
        {
            var playlist = LoadPlaylist();
            if (playlist != null)
            {
                if (playlist.mode == "video")
                {
                    var videos = new List<string>();
                    if (playlist.videoFiles != null)
                    {
                        foreach (string file in playlist.videoFiles)
                        {
                            if (File.Exists(file)) videos.Add(file);
                        }
                    }
                    if (videos.Count == 0 && !string.IsNullOrEmpty(playlist.videoFile) && File.Exists(playlist.videoFile))
                    {
                        videos.Add(playlist.videoFile); // 旧版单视频字段
                    }
                    if (videos.Count > 0)
                    {
                        StartVideoPlaylist(videos);
                        return;
                    }
                }
                if (playlist.mode == "images" && playlist.imageFiles != null && playlist.imageFiles.Count > 0)
                {
                    var existing = new List<string>();
                    foreach (string file in playlist.imageFiles)
                    {
                        if (File.Exists(file)) existing.Add(file);
                    }
                    if (existing.Count > 0)
                    {
                        StartSlideshow(existing, playlist.interval);
                        return;
                    }
                }
            }

            // 旧版本兼容：播放最近接收过的视频
            string latest = _client.FindLatestReceivedVideo();
            if (!string.IsNullOrEmpty(latest) && File.Exists(latest))
            {
                StartVideoPlaylist(new List<string> { latest });
                return;
            }

#if UNITY_ANDROID
            // Android：随包默认视频（jar: URI 原样传给 VideoPlayer）
            StartVideoPlaylist(new List<string> { Application.streamingAssetsPath + "/Videos/videos.mp4" });
#else
#if UNITY_EDITOR
            string editorDefault = Path.Combine(Application.dataPath, "Videos", "videos.mp4");
            if (File.Exists(editorDefault))
            {
                StartVideoPlaylist(new List<string> { editorDefault });
                return;
            }
#endif
            string streaming = Path.Combine(Application.streamingAssetsPath, "Videos", "videos.mp4");
            if (File.Exists(streaming))
            {
                StartVideoPlaylist(new List<string> { streaming });
                return;
            }

            _playState = "未找到默认视频";
            RefreshStatus();
#endif
        }

        /// 播放视频列表：单个视频循环；多个视频按顺序轮播（播完最后一个回到第一个）。
        void StartVideoPlaylist(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return;
            _slideshowActive = false;
            _videoPlaylistActive = true;
            _videoPaths = paths;
            _videoIndex = 0;
            PlayCurrentVideo();
        }

        void PlayCurrentVideo()
        {
            if (_videoPaths == null || _videoPaths.Count == 0) return;
            string path = _videoPaths[_videoIndex];
            bool singleLoop = _videoPaths.Count == 1;

            _rawImage.texture = _renderTexture;
            _fitter.aspectRatio = 16f / 9f;
            _playState = singleLoop
                ? Path.GetFileName(path) + "（循环播放）"
                : $"视频 {_videoIndex + 1}/{_videoPaths.Count}: {Path.GetFileName(path)}";
            RefreshStatus();

            _preparingVideo = true;
            _player.Stop();
            _player.isLooping = singleLoop;
            _player.source = VideoSource.Url;
            // APK 内 assets（jar: URI 或 !assets 形式）必须原样传给 VideoPlayer；普通文件使用 file:// URI
            bool rawUrl = path.StartsWith("jar:", StringComparison.OrdinalIgnoreCase) || path.Contains("!assets");
            _player.url = rawUrl ? path : "file:///" + path.Replace('\\', '/');
            _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _player.SetTargetAudioSource(0, _audio);
            _player.Prepare();
        }

        void StartSlideshow(List<string> paths, float interval)
        {
            _videoPlaylistActive = false;
            _slideshowActive = true;
            _slideshowPaths = paths;
            _slideshowInterval = interval > 0f ? interval : 5f;
            _slideshowIndex = 0;
            _slideshowTimer = 0f;
            _player.Stop();
            ShowSlideshowImage();
        }

        void ShowSlideshowImage()
        {
            if (_slideshowPaths == null || _slideshowPaths.Count == 0) return;
            string path = _slideshowPaths[_slideshowIndex];
            try
            {
                byte[] data = File.ReadAllBytes(path);
                if (!_imageTexture.LoadImage(data))
                {
                    Debug.LogWarning("[ClientApp] 图片解码失败: " + path);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ClientApp] 读取图片失败: " + path + " - " + ex.Message);
            }

            _rawImage.texture = _imageTexture;
            _fitter.aspectRatio = _imageTexture.width > 0 && _imageTexture.height > 0
                ? (float)_imageTexture.width / _imageTexture.height
                : 16f / 9f;
            _playState = _slideshowPaths.Count > 1
                ? $"幻灯片 {_slideshowIndex + 1}/{_slideshowPaths.Count}（每 {_slideshowInterval:0.#} 秒）"
                : Path.GetFileName(path);
            RefreshStatus();
        }

        void SavePlaylistVideos(List<string> files)
        {
            SavePlaylist(new PlaylistData { mode = "video", videoFile = null, videoFiles = new List<string>(files), imageFiles = null, interval = 0f });
        }

        void SavePlaylistImages(List<string> files, float interval)
        {
            SavePlaylist(new PlaylistData { mode = "images", videoFile = null, imageFiles = new List<string>(files), interval = interval });
        }

        void SavePlaylist(PlaylistData data)
        {
            try
            {
                string path = PlaylistPath;
                if (string.IsNullOrEmpty(path)) return;
                Directory.CreateDirectory(_client.SaveDirectory);
                File.WriteAllText(path, JsonUtility.ToJson(data));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ClientApp] 保存播放列表失败: " + ex.Message);
            }
        }

        PlaylistData LoadPlaylist()
        {
            try
            {
                string path = PlaylistPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return JsonUtility.FromJson<PlaylistData>(File.ReadAllText(path));
            }
            catch { return null; }
        }

        /// 清理两个接收目录中未被当前媒体引用的历史文件（被占用时跳过，下次再清理）。
        void CleanupUnreferencedMedia(object keep)
        {
            try
            {
                var keepSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (keep is string file) keepSet.Add(file);
                else if (keep is List<string> files)
                {
                    foreach (string f in files) keepSet.Add(f);
                }

                CleanDirectory(_client.ReceivedDirectory, keepSet);
                CleanDirectory(_client.ReceivedImagesDirectory, keepSet);
            }
            catch { }
        }

        static void CleanDirectory(string directory, HashSet<string> keepSet)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory))
            {
                if (keepSet.Contains(file)) continue;
                try { File.Delete(file); } catch { }
            }
        }

        /// <summary>
        /// 播放 / 继续
        /// </summary>
        public void Play()
        {
            if(_slideshowActive) return;
            if (!isPrepared) return;

            if (_player.isPaused)
                _player.Play();       // 恢复播放
            else if (!_player.isPlaying)
                _player.Play();       // 从头或继续
            else
                _player.Play();

        }

        /// <summary>
        /// 暂停
        /// </summary>
        public void Pause()
        {
            if(_slideshowActive) return;
            if (_player.isPlaying)
            {
                _player.Pause();
            }
        }
        /// <summary>
        /// 重播（从头开始）
        /// </summary>
        public void Restart()
        {
            if(_slideshowActive) return;
            if (!isPrepared) return;

            _player.Stop();
            _player.time = 0f;
            _player.Play();
            SetVolume(1);
            SetMute(false);
        }
        /// <summary>
        /// 播放 / 暂停 切换
        /// </summary>
        public void TogglePlayPause()
        {
            if(_slideshowActive) return;
            if (_player.isPlaying)
                Pause();
            else
                Play();
        }
        /// <summary>
        /// 增加音量
        /// </summary>
        public void VolumeUp()
        {
            SetVolume(currentVolume + volumeStep);
        }

        /// <summary>
        /// 减小音量
        /// </summary>
        public void VolumeDown()
        {
            SetVolume(currentVolume - volumeStep);
        }

        /// <summary>
        /// 设置音量（0~1）
        /// </summary>
        public void SetVolume(float value)
        {
            if(_slideshowActive) return;
            currentVolume = Mathf.Clamp01(value);
            AudioSource audioSource = _player.GetTargetAudioSource(0);
            if (audioSource != null) 
                audioSource.volume = currentVolume;
        }

        /// <summary>
        /// 静音
        /// </summary>
        /// <param name="mute"></param>
        public void SetMute(bool mute)
        {
            if(_slideshowActive) return;
            AudioSource audioSource = _player.GetTargetAudioSource(0);
            if (audioSource != null)
                audioSource.mute = mute;
        }

    }
}
