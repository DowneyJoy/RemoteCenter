using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace RemoteCenter
{
    /// 服务端（控制端后台）：运行时生成控制面板，选择本地视频并通过 TCP 下发给全部或指定客户端。
    public class ServerApp : MonoBehaviour
    {
        const int MaxLogLines = 200;
        const int MaxTargetRows = 8; // “全部客户端” + 最多 7 个客户端行
        const float RowHeight = 38f;

        readonly ConcurrentQueue<Action> _mainQueue = new ConcurrentQueue<Action>();
        readonly List<string> _logLines = new List<string>(); 
        readonly List<Button> _targetRows = new List<Button>();
        readonly List<string> _targetRowValues = new List<string>();
        readonly List<Tuple<RectTransform, float>> _layout = new List<Tuple<RectTransform, float>>();

        VideoServer _server;
        Button _uploadButton;
        Button _sendButton;
        Text _fileInfo;
        Text _status;
        Text _log;
        Text _progressText;
        Text _progressLabel;
        Slider _progress;
        InputField _intervalInput;
        InputField _serverIpInput;
        InputField _targetIpInput;
        ScrollRect _logScroll;
        RectTransform _logContent;
        RectTransform _logPanelRT;
        Transform _uiRoot;
        List<string> _selectedFiles;
        long _selectedTotalSize;
        string _targetEndpoint; // null = 全部客户端

        static readonly Color RowColorNormal = new Color(0.23f, 0.27f, 0.35f, 1f);
        static readonly Color RowColorSelected = new Color(0.20f, 0.55f, 0.32f, 1f);

        void Start()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.055f, 0.075f, 0.11f, 1f);
            }

            BuildUI();

            _server = new VideoServer(VideoTransferProtocol.Port);
            _server.Log += m => Post(() => AppendLog(m));
            _server.ClientsChanged += endpoints => Post(() =>
            {
                _status.text = $"监听 0.0.0.0:{VideoTransferProtocol.Port}  |  已连接客户端: {endpoints.Count}";
                RebuildTargetRows(endpoints);
            });
            _server.SendProgress += (done, total, label) => Post(() =>
            {
                _progressLabel.text = label;
                _progress.value = total > 0 ? (float)((double)done / total) : 0f;
                _progressText.text = $"{(total > 0 ? done * 100 / total : 0)}%  ({FormatMB(done)} / {FormatMB(total)})";
            });
            _server.SendCompleted += m => Post(() =>
            {
                _sendButton.interactable = _selectedFiles != null && _selectedFiles.Count > 0;
                _uploadButton.interactable = true;
                _progressText.text = m;
                AppendLog(m);
            });
            _server.Start();

            RebuildTargetRows(new List<string>());
            AppendLog($"服务端已启动，监听端口 {VideoTransferProtocol.Port}，等待客户端连接...");
        }

        void Update()
        {
            while (_mainQueue.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        void OnDestroy()
        {
            _server?.Dispose();
        }

        void Post(Action action) => _mainQueue.Enqueue(action);

        void AppendLog(string message)
        {
            _logLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (_logLines.Count > MaxLogLines) _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
            if (_log == null) return;

            // 记录追加前是否贴在底部，贴底时保持自动滚动到最新
            bool stickToBottom = _logScroll != null && _logScroll.verticalNormalizedPosition <= 0.02f;
            _log.text = string.Join("\n", _logLines);

            if (_logContent != null)
            {
                float height = Mathf.Max(_log.preferredHeight+20f, 40f);
                _logContent.sizeDelta = new Vector2(0f, height);
                Debug.Log("Log height:"+height+" | _log.preferredHeight:"+_log.preferredHeight);
            }
            if (_logScroll != null && stickToBottom) _logScroll.verticalNormalizedPosition = 0f;
        }

        static string FormatMB(long bytes) => $"{bytes / 1048576.0:0.00} MB";

        void OnInitSingleClicked()
        {
            string targetIp = _targetIpInput != null ? _targetIpInput.text.Trim() : "";
            if (targetIp.Length == 0 || !System.Net.IPAddress.TryParse(targetIp, out _))
            {
                AppendLog("初始化失败：请输入有效的客户端 IP");
                return;
            }
            string localIp = ReadServerIpInput();
            if (localIp == null)
            {
                AppendLog("初始化失败：服务端 IP 无效，请在“服务端 IP”输入框中填写正确地址");
                return;
            }
            AppendLog($"向 {targetIp} 发送初始化命令（服务端 IP: {localIp}，重复 3 次）");
            SendInitOnBackground(localIp, targetIp, false);
        }

        void OnInitBroadcastClicked()
        {
            string localIp = ReadServerIpInput();
            if (localIp == null)
            {
                AppendLog("初始化失败：服务端 IP 无效，请在“服务端 IP”输入框中填写正确地址");
                return;
            }
            AppendLog($"向局域网广播初始化命令（服务端 IP: {localIp}，重复 3 次）");
            SendInitOnBackground(localIp, null, true);
        }
        
        void OnDisconnectIpClicked()
        {
            string targetIp = _targetIpInput != null ? _targetIpInput.text.Trim() : "";
            if (targetIp.Length == 0 || !System.Net.IPAddress.TryParse(targetIp, out _))
            {
                AppendLog("断开失败：请输入有效的客户端 IP");
                return;
            }
            _server.DisconnectClients(targetIp);
        }

        void OnDisconnectAllClicked()
        {
            _server.DisconnectAll();
        }
        
        /// 读取“服务端 IP”输入框；支持用户手动修正（如自动检测不准确时）。
        string ReadServerIpInput()
        {
            string ip = _serverIpInput != null ? _serverIpInput.text.Trim() : "";
            return ip.Length > 0 && System.Net.IPAddress.TryParse(ip, out _) ? ip : null;
        }

        void SendInitOnBackground(string localIp, string targetIp, bool broadcast)
        {
            var thread = new System.Threading.Thread(() =>
            {
                if (broadcast) InitCommandSender.SendBroadcast(localIp);
                else InitCommandSender.SendUnicast(localIp, targetIp);
            })
            { IsBackground = true, Name = "InitCommandSend" };
            thread.Start();
        }

        void BuildUI()
        {
            var root = UIFactory.CreateCanvas("ServerCanvas").transform;
            _uiRoot = root;

            var title = UIFactory.CreateText(root, "Title", "远程视频控制端（服务端）", 30, Color.white, TextAnchor.MiddleLeft);
            UIFactory.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -36), new Vector2(1000, 44));

            _status = UIFactory.CreateText(root, "Status", $"监听 0.0.0.0:{VideoTransferProtocol.Port}  |  已连接客户端: 0", 20, new Color(0.85f, 0.9f, 1f), TextAnchor.MiddleLeft);
            UIFactory.Place(_status.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -84), new Vector2(1000, 36));

            _uploadButton = UIFactory.CreateButton(root, "UploadButton", "上传视频/图片", 23);
            UIFactory.Place((RectTransform)_uploadButton.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -150), new Vector2(240, 60));
            _uploadButton.onClick.AddListener(OnUploadClicked);

            _sendButton = UIFactory.CreateButton(root, "SendButton", "发送到客户端", 23);
            UIFactory.Place((RectTransform)_sendButton.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -230), new Vector2(240, 60));
            _sendButton.interactable = false;
            _sendButton.onClick.AddListener(OnSendClicked);

            var intervalLabel = UIFactory.CreateText(root, "IntervalLabel", "轮播间隔(秒):", 18, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleRight);
            UIFactory.Place(intervalLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(300, -242), new Vector2(180, 36));
            _intervalInput = UIFactory.CreateInputField(root, "IntervalInput", "5", "5");
            UIFactory.Place((RectTransform)_intervalInput.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(490, -242), new Vector2(70, 36));

            _fileInfo = UIFactory.CreateText(root, "FileInfo", "未选择文件（视频单选，图片可多选）", 19, new Color(0.8f, 0.85f, 0.9f), TextAnchor.UpperLeft);
            UIFactory.Place(_fileInfo.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -316), new Vector2(520, 76));

            var targetLabel = UIFactory.CreateText(root, "TargetLabel", "发送目标（点击选择）:", 18, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleLeft);
            UIFactory.Place(targetLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -400), new Vector2(520, 24));

            // 客户端初始化 / 连接管理区（右列）：服务端 IP 可编辑（预填自动检测值），目标客户端 IP + 四个操作按钮
            var initTitle = UIFactory.CreateText(root, "InitTitle", "客户端初始化 / 连接管理", 20, Color.white, TextAnchor.MiddleLeft);
            UIFactory.Place(initTitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(600, -150), new Vector2(620, 30));

            var serverIpLabel = UIFactory.CreateText(root, "ServerIpLabel", "服务端 IP:", 18, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleRight);
            UIFactory.Place(serverIpLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(600, -196), new Vector2(110, 40));
            _serverIpInput = UIFactory.CreateInputField(root, "ServerIpInput", InitCommandSender.GetLocal192168Ipv4() ?? "127.0.0.1", "本机服务端 IP");
            UIFactory.Place((RectTransform)_serverIpInput.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(715, -196), new Vector2(150, 40));

            var targetIpLabel = UIFactory.CreateText(root, "TargetIpLabel", "客户端 IP:", 18, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleRight);
            UIFactory.Place(targetIpLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(880, -196), new Vector2(110, 40));
            _targetIpInput = UIFactory.CreateInputField(root, "TargetIpInput", "", "目标客户端 IP");
            UIFactory.Place((RectTransform)_targetIpInput.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(995, -196), new Vector2(150, 40));

            var initSingleButton = UIFactory.CreateButton(root, "InitSingleButton", "初始化指定 IP", 18);
            UIFactory.Place((RectTransform)initSingleButton.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(600, -250), new Vector2(215, 40));
            initSingleButton.onClick.AddListener(OnInitSingleClicked);

            var initBroadcastButton = UIFactory.CreateButton(root, "InitBroadcastButton", "广播到局域网", 18);
            UIFactory.Place((RectTransform)initBroadcastButton.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(825, -250), new Vector2(170, 40));
            initBroadcastButton.onClick.AddListener(OnInitBroadcastClicked);

            var disconnectIpButton = UIFactory.CreateButton(root, "DisconnectIpButton", "断开指定 IP", 18);
            UIFactory.Place((RectTransform)disconnectIpButton.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(1005, -250), new Vector2(170, 40));
            disconnectIpButton.onClick.AddListener(OnDisconnectIpClicked);

            var disconnectAllButton = UIFactory.CreateButton(root, "DisconnectAllButton", "断开所有 IP", 18);
            UIFactory.Place((RectTransform)disconnectAllButton.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(1185, -250), new Vector2(170, 40));
            disconnectAllButton.onClick.AddListener(OnDisconnectAllClicked);

            var initHint = UIFactory.CreateText(root, "InitHint",
                "初始化：未连接的客户端收到后保存 IP 并连接。断开：向已连接客户端下发断开命令，客户端停止重连。",
                15, new Color(0.62f, 0.68f, 0.76f), TextAnchor.UpperLeft);
            UIFactory.Place(initHint.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(600, -300), new Vector2(760, 26));
            
            _progressLabel = UIFactory.CreateText(root, "ProgressLabel", "发送进度", 18, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleLeft);
            UIFactory.Place(_progressLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -600), new Vector2(520, 26));
            _layout.Add(Tuple.Create(_progressLabel.rectTransform, -600f));

            _progress = UIFactory.CreateSlider(root, "Progress");
            UIFactory.Place((RectTransform)_progress.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -632), new Vector2(520, 20));
            _layout.Add(Tuple.Create((RectTransform)_progress.transform, -632f));

            _progressText = UIFactory.CreateText(root, "ProgressText", "等待发送", 18, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleLeft);
            UIFactory.Place(_progressText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -664), new Vector2(520, 26));
            _layout.Add(Tuple.Create(_progressText.rectTransform, -664f));

            var logTitle = UIFactory.CreateText(root, "LogTitle", "运行日志", 20, Color.white, TextAnchor.MiddleLeft);
            UIFactory.Place(logTitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -706), new Vector2(400, 30));
            _layout.Add(Tuple.Create(logTitle.rectTransform, -706f));

            // 运行日志：ScrollRect + RectMask2D，支持鼠标滚轮上下滚动
            var logGo = new GameObject("LogPanel", typeof(Image), typeof(ScrollRect));
            logGo.transform.SetParent(root, false);
            var logBg = logGo.GetComponent<Image>();
            logBg.color = new Color(0.03f, 0.03f, 0.05f, 0.8f);
            UIFactory.Stretch(logGo.GetComponent<RectTransform>());
            _logPanelRT = logGo.GetComponent<RectTransform>();
            _logPanelRT.offsetMin = new Vector2(40, 32);
            _logPanelRT.offsetMax = new Vector2(-40, -740);

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(logGo.transform, false);
            UIFactory.Stretch(viewportGo.GetComponent<RectTransform>());

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            _logContent = contentGo.GetComponent<RectTransform>();
            _logContent.anchorMin = new Vector2(0, 1);
            _logContent.anchorMax = new Vector2(1, 1);
            _logContent.pivot = new Vector2(0.5f, 1f);
            _logContent.anchoredPosition = Vector2.zero;
            _logContent.sizeDelta = new Vector2(0, 40);

            _log = UIFactory.CreateText(contentGo.transform, "Log", "", 16, new Color(0.75f, 0.8f, 0.85f), TextAnchor.UpperLeft);
            UIFactory.Stretch(_log.rectTransform);
            _log.rectTransform.offsetMin = new Vector2(14, 10);
            _log.rectTransform.offsetMax = new Vector2(-14, -10);

            _logScroll = logGo.GetComponent<ScrollRect>();
            _logScroll.viewport = viewportGo.GetComponent<RectTransform>();
            _logScroll.content = _logContent;
            _logScroll.horizontal = false;
            _logScroll.vertical = true;
            _logScroll.movementType = ScrollRect.MovementType.Clamped;
            _logScroll.scrollSensitivity = 40f;
        }

        void RebuildTargetRows(List<string> endpoints)
        {
            foreach (var row in _targetRows)
            {
                if (row != null) Destroy(row.gameObject);
            }
            _targetRows.Clear();
            _targetRowValues.Clear();

            // 选中的目标已断开时回退到“全部客户端”
            if (_targetEndpoint != null && !endpoints.Contains(_targetEndpoint))
            {
                _targetEndpoint = null;
                AppendLog("所选发送目标已断开，回退为：全部客户端");
            }

            AddTargetRow("全部客户端", null, 0);
            int shown = 0;
            for (int i = 0; i < endpoints.Count && shown < MaxTargetRows - 1; i++, shown++)
            {
                AddTargetRow(endpoints[i], endpoints[i], shown + 1);
            }
            if (endpoints.Count > MaxTargetRows - 1)
            {
                AppendLog($"客户端数量较多，发送目标列表仅显示前 {MaxTargetRows - 1} 个");
            }

            RefreshRowColors();
            ApplyLayout(_targetRows.Count);
        }

        void AddTargetRow(string label, string value, int index)
        {
            var row = UIFactory.CreateButton(_uiRoot, "Target_" + index, label, 18);
            UIFactory.Place((RectTransform)row.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -430 - index * RowHeight), new Vector2(520, 34));
            string captured = value;
            row.onClick.AddListener(() => OnTargetSelected(captured));
            _targetRows.Add(row);
            _targetRowValues.Add(value);
        }

        void OnTargetSelected(string value)
        {
            _targetEndpoint = value;
            RefreshRowColors();
            AppendLog(value == null ? "发送目标：全部客户端" : $"发送目标：{value}");
        }

        void RefreshRowColors()
        {
            for (int i = 0; i < _targetRows.Count; i++)
            {
                bool selected = string.Equals(_targetRowValues[i], _targetEndpoint, StringComparison.Ordinal);
                _targetRows[i].GetComponent<Image>().color = selected ? RowColorSelected : RowColorNormal;
            }
        }

        /// 目标行数量变化时，下移进度区与日志面板，避免遮挡。
        void ApplyLayout(int rowCount)
        {
            float shift = Math.Max(0, rowCount - 1) * RowHeight;
            foreach (var entry in _layout)
            {
                var rt = entry.Item1;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, entry.Item2 - shift);
            }
            _logPanelRT.offsetMax = new Vector2(-40, -(740 + shift));
        }

        static bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png";
        }

        float ParseSlideshowInterval()
        {
            float value = 5f;
            if (float.TryParse(_intervalInput != null ? _intervalInput.text.Trim() : "", out float parsed)) value = parsed;
            return Mathf.Clamp(value, 1f, 3600f);
        }

        void OnUploadClicked()
        {
            string[] picked = Win32FilePicker.PickMediaFiles();
            if (picked == null || picked.Length == 0)
            {
                AppendLog("未选择文件");
                return;
            }

            var files = new List<string>();
            long total = 0;
            foreach (string path in picked)
            {
                if (!File.Exists(path)) continue;
                files.Add(path);
                total += new FileInfo(path).Length;
            }
            if (files.Count == 0)
            {
                AppendLog("所选文件不存在，请重新上传");
                return;
            }

            _selectedFiles = files;
            _selectedTotalSize = total;

            var names = new List<string>();
            foreach (string path in files) names.Add(Path.GetFileName(path));
            string nameSummary = names.Count <= 3
                ? string.Join("、", names)
                : string.Join("、", names.GetRange(0, 3)) + $" 等 {names.Count} 个";

            _fileInfo.text = files.Count == 1
                ? $"{files[0]}\n大小: {FormatMB(total)}"
                : $"已选 {files.Count} 张图片（共 {FormatMB(total)}）：{nameSummary}";
            _sendButton.interactable = true;
            AppendLog(files.Count == 1
                ? $"已选择文件: {nameSummary}（{FormatMB(total)}）"
                : $"已选择 {files.Count} 张图片（共 {FormatMB(total)}）：{nameSummary}");
        }

        void OnSendClicked()
        {
            if (_selectedFiles == null || _selectedFiles.Count == 0)
            {
                AppendLog("未选择文件，请先上传");
                return;
            }
            if (System.Linq.Enumerable.Any(_selectedFiles, f => !File.Exists(f)))
            {
                AppendLog("所选文件已不存在，请重新上传");
                return;
            }

            int imageCount = 0;
            foreach (string file in _selectedFiles)
            {
                if (IsImageFile(file)) imageCount++;
            }

            // 分流：单视频 / 单张图片 / 多张图片批次；视频与图片混合则拒绝
            if (_selectedFiles.Count > 1 && imageCount != _selectedFiles.Count)
            {
                AppendLog("多选时只能选择图片；一次只能发送一个视频或若干张图片");
                return;
            }

            _sendButton.interactable = false;
            _uploadButton.interactable = false;
            _progress.value = 0f;
            _progressText.text = "准备发送...";
            string target = _targetEndpoint == null ? "全部客户端" : _targetEndpoint;
            float interval = ParseSlideshowInterval();

            if (_selectedFiles.Count == 1 && imageCount == 0)
            {
                AppendLog($"开始发送视频到 {target}: {Path.GetFileName(_selectedFiles[0])}");
                if (_targetEndpoint == null) _server.SendToClients(_selectedFiles[0]);
                else _server.SendToEndpoint(_targetEndpoint, _selectedFiles[0]);
            }
            else
            {
                AppendLog($"开始发送 {_selectedFiles.Count} 张图片到 {target}（每 {interval:0.#} 秒轮播）");
                if (_targetEndpoint == null) _server.SendImagesToClients(_selectedFiles.ToArray(), interval);
                else _server.SendImagesToEndpoint(_targetEndpoint, _selectedFiles.ToArray(), interval);
            }
        }
    }
}
