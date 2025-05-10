using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.RegularExpressions;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using System.Text;
using System.Runtime.InteropServices;

public class TranslatorTask
{
    public class TaskData
    {
        public enum TaskState
        {
            Waiting,
            Processing,
            Completed,
            Failed
        }
        public string[] texts { get; set; }
        public string[]? result { get; set; } = null;
        public int reqID { get; set; }
        public int retryCount { get; set; } = 0;

        private TaskState _state = TaskState.Waiting;
        private readonly object _stateLock = new object();
        private readonly TaskCompletionSource<bool> _completionSource = new TaskCompletionSource<bool>();

        public TaskState state
        {
            get
            {
                lock (_stateLock) return _state;
            }
            set
            {
                lock (_stateLock)
                {
                    _state = value;
                    if (_state == TaskState.Completed || _state == TaskState.Failed)
                    {
                        _completionSource.TrySetResult(true);
                    }
                }
            }
        }

        public Task WaitOne()
        {
            return _completionSource.Task;
        }
    }


    private string? _apiKey;
    private string? _model;
    private string? _requirement;
    private string? _url;
    private string? _terminology;
    private string? _gameName;
    private string? _gameDesc;
    private int _maxWordCount = 2000;
    private int _parallelCount = 10;
    private int _pollingInterval = 1000;
    private string? DestinationLanguage;
    private string? SourceLanguage;
    //使用半角符号
    private bool _halfWidth = true;
    private int _maxRetry = 10;
    private string _modelParams = "";
    List<TaskData> taskDatas = new List<TaskData>();
    TranslateDB translateDB = new TranslateDB();
    //Dictionary<string, string> translateData = new Dictionary<string, string>();
    HttpListener listener = new HttpListener();
    //最近翻译
    List<string> recentTranslate = new List<string>();

    void Log(string txt)
    {
        Logger.Log(txt);
    }

    public void Init(IInitializationContext context)
    {
        _apiKey = context.GetOrCreateSetting("AutoLLM", "APIKey", "");
        _model = context.GetOrCreateSetting("AutoLLM", "Model", "gpt-4o");
        _requirement = context.GetOrCreateSetting("AutoLLM", "Requirement", "");
        _url = context.GetOrCreateSetting("AutoLLM", "URL", "https://api.openai.com/v1/chat/completions");
        _terminology = context.GetOrCreateSetting("AutoLLM", "Terminology", "");
        _gameName = context.GetOrCreateSetting("AutoLLM", "GameName", "A Game");
        _gameDesc = context.GetOrCreateSetting("AutoLLM", "GameDesc", "");
        _maxWordCount = context.GetOrCreateSetting("AutoLLM", "MaxWordCount", 500);
        _parallelCount = context.GetOrCreateSetting("AutoLLM", "ParallelCount", 3);
        _pollingInterval = context.GetOrCreateSetting("AutoLLM", "Interval", 200);
        _halfWidth = context.GetOrCreateSetting("AutoLLM", "HalfWidth", true);
        _maxRetry = context.GetOrCreateSetting("AutoLLM", "MaxRetry", 10);
        _modelParams = context.GetOrCreateSetting("AutoLLM", "ModelParams", "");

        if (_url.EndsWith("/v1"))
        {
            _url += "/chat/completions";
        }
        if (_url.EndsWith("/v1/"))
        {
            _url += "chat/completions";
        }

        DestinationLanguage = context.DestinationLanguage;
        SourceLanguage = context.SourceLanguage;
        if (string.IsNullOrEmpty(_apiKey) && !_url.Contains("localhost") && !_url.Contains("127.0.0.1") && !_url.Contains("192.168."))
        {
            throw new Exception("The AutoLLM endpoint requires an API key which has not been provided.");
        }
        translateDB.Init(context, _terminology);

        listener = new HttpListener();
        listener.Prefixes.Add("http://+:20000/");
        // 启动监听
        listener.Start();
        Log("Listening for requests on http://localhost:20000/");

        // Start a separate thread for HTTP listener
        Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    // Wait for incoming request
                    var context = await listener.GetContextAsync();
                    // Process request
                    ProcessRequest(context);
                }
            }
            catch (Exception ex)
            {
                Log($"HTTP listener error: {ex.Message}");
            }
        });
        Task.Run(() => Polling());
    }

    async private void ProcessRequest(HttpListenerContext context)
    {
        try
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            if (request.HttpMethod == "POST")
            {
                // 读取请求体
                using (Stream body = request.InputStream)
                using (StreamReader reader = new StreamReader(body, request.ContentEncoding))
                {
                    string requestBody = reader.ReadToEnd();
                    //Log($"Received POST request with body: {requestBody}");
                    var requestData = JObject.Parse(requestBody);
                    var texts = requestData["texts"]?.ToObject<string[]>() ?? new string[0];
                    var task = await AddTask(texts);
                    var rs = new
                    {
                        texts = task.result
                    };
                    // 返回响应
                    string responseString = JsonConvert.SerializeObject(rs);
                    byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
            }
            if (request.HttpMethod == "GET")
            {
                // 处理 GET 请求
                string responseString = "AutoLLM Translator is running.";
                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
        }
        catch (Exception ex)
        {
            Log($"处理请求时发生错误: {ex.Message}");
        }
        finally
        {
            // 关闭响应
            context.Response.Close();
        }
    }

    List<TaskData> SelectTasks()
    {
        var tasks = new List<TaskData>();
        int toltoken = 0;
        lock (_lockObject)
        {
            foreach (var task in taskDatas)
            {
                if (task.state == TaskData.TaskState.Waiting)
                {
                    int taskToken = 0;
                    foreach (var txt in task.texts)
                    {
                        taskToken += txt.Length;
                    }
                    if (toltoken + taskToken > _maxWordCount && tasks.Count > 0)
                    {
                        break;
                    }
                    if (task.retryCount > 2 && tasks.Count > 0)
                    {
                        continue;
                    }
                    toltoken += taskToken;
                    tasks.Add(task);
                    if (task.retryCount > 0)//错过就单独处理
                        break;
                    //task.state = TaskData.TaskState.Processing;
                }
            }
        }
        return tasks;
    }

    async public Task<TaskData> AddTask(string[] texts)
    {
        var task = new TaskData() { texts = texts };

        // 添加任务时上锁
        lock (_lockObject)
        {
            taskDatas.Insert(0, task);
        }

        // 等待任务完成
        await task.WaitOne();

        // 移除任务时上锁
        lock (_lockObject)
        {
            taskDatas.Remove(task);
        }

        return task;
    }

    private string EscapeSpecialCharacters(string text)
    {
        //将换行进行转义
        text = text.Replace("\n", "\\n");
        text = text.Replace("\r", "\\r");
        text = text.Replace("\"", "<quote>");
        return text;
    }

    private string UnEscapeSpecialCharacters(string text)
    {
        //将换行进行转义
        text = text.Replace("\\n", "\n");
        text = text.Replace("\\r", "\r");
        text = text.Replace("<quote>", "\"");
        return text;
    }

    int curProcessingCount = 0;
    private readonly object _lockObject = new object();

    async Task ProcessTaskBatch(List<TaskData> tasks)
    {
        int hashkey = tasks.GetHashCode();
        try
        {
            //Log($"翻译开始Batch:" + hashkey);
            foreach (var task in tasks)
            {
               Log($"{hashkey} 翻译开始:{task.texts[0]}");
            }
            List<string> texts = new List<string>();
            foreach (var task in tasks)
            {
                texts.AddRange(task.texts);
            }
            var system = Config.prompt_base
            .Replace("{{GAMENAME}}", _gameName)
            .Replace("{{GAMEDESC}}", _gameDesc)
            .Replace("{{OTHER}}", _requirement)
            .Replace("{{HISTORY}}", string.Join("\n", translateDB.Search(texts, 1000)))
            .Replace("{{TARGET_LAN}}", DestinationLanguage)
            .Replace("{{SOURCE_LAN}}", SourceLanguage)
            .Replace("{{RECENT}}", string.Join("\n", recentTranslate));
            var otxt = "";
            int index = 1;
            foreach (var data in texts)
            {
                var t = EscapeSpecialCharacters(data);
                otxt += $"[{index}]=\"{t}\"\n";
                index++;
            }
           // otxt += "]";
            if (system.Contains("/no_think") || system.Contains("/nothink"))
            {
                otxt = otxt + "\n/no_think";
            }
            var messages = new List<object>
            {
                new { role = "system", content = system },
                new { role = "user", content = otxt}
            };

            var requestBody = new Dictionary<string, object>
            {
                { "model", _model },
                { "temperature", 0.1 },
                { "max_tokens", 4000 },
                { "top_p", 1 },
                { "frequency_penalty", 0 },
                { "presence_penalty", 0 },
                { "messages", messages }
            };
            if (!string.IsNullOrEmpty(_modelParams))
            {
                try
                {
                    var modelParamsData = JsonConvert.DeserializeObject<JObject>(_modelParams);
                    if (modelParamsData != null)
                    {
                        foreach (var item in modelParamsData)
                            if (item.Value != null)
                            {
                                requestBody[item.Key] = item.Value;
                            }
                    }
                }
                catch (JsonReaderException ex)
                {
                    Log($"模型参数解析错误: {ex.Message}");
                }
            }

            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.Authorization] = $"Bearer {_apiKey}";
                client.Headers[HttpRequestHeader.ContentType] = "application/json";

                // 创建HTTP请求
                var request = (HttpWebRequest)WebRequest.Create(_url);
                request.Method = "POST";
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                request.ContentType = "application/json";

                // 写入请求体
                requestBody.Add("stream", true);
                var requestJson = JsonConvert.SerializeObject(requestBody);
                //Log($"请求: {requestJson}");
                using (var streamWriter = new StreamWriter(request.GetRequestStream()))
                {
                    await streamWriter.WriteAsync(requestJson);
                }

                // 获取响应
                using (var response = (HttpWebResponse)await request.GetResponseAsync())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    var lineResponse = "";
                    var fullResponse = "";
                    string? line;
                    var i = 0;                    
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        if (!line.StartsWith("data: ")) continue;

                        var data = line.Substring(6);
                        if (data == "[DONE]") break;

                        try
                        {
                            var json = JObject.Parse(data);
                            var content = json["choices"]?[0]?["delta"]?["content"]?.ToString();
                            if (!string.IsNullOrEmpty(content))
                            {
                                lineResponse += content;
                                //Log($"流: {content} ::: {lineResponse}");
                                fullResponse += content;
                                if (lineResponse.Contains("</think>"))
                                    lineResponse = Regex.Replace(lineResponse, "<think>.*?</think>", "", RegexOptions.Singleline);
                                if (lineResponse.Contains("</context_think>"))
                                    lineResponse = Regex.Replace(lineResponse, "<context_think>.*?</context_think>", "", RegexOptions.Singleline);
                                if (string.IsNullOrEmpty(lineResponse))
                                    continue;                                
                                Log($"{hashkey} 流0: {lineResponse}");
                                var lineResponseTxts = lineResponse.Split('\n');
                                int point = 0;
                                foreach (var txt in lineResponseTxts)
                                {
                                    point += txt.Length;
                                    var rs = txt.Trim();
                                    Log($"{hashkey} 流1: {rs}");
                                    if (rs.Count(c => c == '\"') < 2)
                                    {
                                        continue;
                                    }
                                    //Log($"{hashkey} 流0_2: {rs}");
                                    if (rs.EndsWith("\"")
                                       // || rs.EndsWith("\",")
                                       // || rs.EndsWith("\"]")
                                       // || rs.EndsWith("\",]")
                                        )
                                    {                                        
                                        Log($"{hashkey} 流2: {rs}");
                                        //找到[NUM]="TEXT"中间的NUM和TEXT
                                        var match = Regex.Match(rs, @"\[(\d+)\]=""(.*?)""");
                                        if (!match.Success)
                                        {
                                            throw new Exception($"翻译结果错误 1: {fullResponse}");
                                        }
                                        var num =-1;
                                        int.TryParse(match.Groups[1].Value, out num);
                                        if (num < 1 || num > tasks.Count)
                                        {
                                            throw new Exception($"翻译结果错误 2: {fullResponse}");
                                        }
                                        rs = match.Groups[2].Value;   
                                        if (string.IsNullOrEmpty(rs))
                                        {
                                            throw new Exception($"翻译结果错误 3: {fullResponse}");
                                        }
                                        if (_halfWidth)
                                        {
                                            //将全角符号转换为半角符号
                                            rs = Regex.Replace(rs, @"[！＂＃＄％＆＇（）＊＋，－．／０１２３４５６７８９：；＜＝＞？＠［＼］＾＿｀｛｜｝～]", m => ((char)(m.Value[0] - 0xFEE0)).ToString());
                                        }
                                        rs = UnEscapeSpecialCharacters(rs);
                                        //Log($"流2: {lineResponse}");
                                        var task = tasks[num - 1];
                                        task.result = new string[] { translateDB.FindTerminology(task.texts[0]) ?? rs };
                                        task.state = TaskData.TaskState.Completed;
                                        Log($"{hashkey} 流OK: {rs}");
                                        lock (recentTranslate)
                                        {
                                            if (recentTranslate.Count > 10)
                                                recentTranslate.RemoveAt(0);
                                            recentTranslate.Add($"{task.texts[0]} === {task.result[0]}");
                                        }
                                        if (translateDB.AddData(task.texts[0], task.result[0]))
                                            translateDB.SortData();
                                        i++;
                                        lineResponse = lineResponse.Substring(point + 1);
                                        point = 0;
                                        Log($"{hashkey} 流截取后: {lineResponse}");
                                    }
                                }
                            }
                        }
                        catch (JsonReaderException ex)
                        {
                            Log($"解析流响应出错: {ex.Message}");
                        }
                    }

                    Log($"full流: {fullResponse}");
                }
            }

        }
        catch (Exception ex)
        {
            Log($"Batch翻译失败: {ex.Message}");
        }
        finally
        {
            Log($"翻译结束:" + hashkey);
            foreach (var task in tasks)
            {
                //失败了重新翻译
                if (task.state != TaskData.TaskState.Completed)
                {
                    task.retryCount++;
                    if (task.retryCount < _maxRetry)
                    {
                        Log($"重新翻译:" + task.texts[0]);
                        task.state = TaskData.TaskState.Waiting;
                        task.result = null;
                    }
                    else
                    {
                        Log($"重试翻译依然失败，没救了:" + task.texts[0]);
                        task.state = TaskData.TaskState.Failed;
                    }
                }
            }
            lock (_lockObject)
                curProcessingCount--;
        }
    }

    //轮询
    // 优化 Polling 方法
    public async Task Polling()
    {
        while (true)
        {
            try
            {
                await Task.Delay(_pollingInterval);
                if (curProcessingCount > 0)
                    Log($"Polling curProcessingCount: {curProcessingCount}/{_parallelCount} TASKS: {taskDatas.Count}");
                if (curProcessingCount >= _parallelCount)
                {
                    continue;
                }
                List<List<TaskData>> taskDatass = new List<List<TaskData>>();
                List<TaskData> tasks;
                lock (_lockObject)
                {
                    tasks = SelectTasks();
                    //Log($"Polling SelectTasks: {tasks.Count}");
                    while (tasks.Count > 0 && curProcessingCount < _parallelCount)
                    {
                        curProcessingCount++;
                        foreach (var task in tasks)
                        {
                            task.state = TaskData.TaskState.Processing;
                        }
                        taskDatass.Add(tasks);
                        tasks = SelectTasks();
                    }
                }

                // 在锁外启动任务处理
                if (taskDatass.Count > 0)
                {
                    foreach (var tasklist in taskDatass)
                    {
                        _ = ProcessTaskBatch(tasklist);
                    }
                }
                // Log("Polling End");
            }
            catch (Exception ex)
            {
                Logger.Log(ex.Message);
            }
        }
    }
}