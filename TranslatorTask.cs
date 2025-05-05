using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.RegularExpressions;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using System.Text;

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
        _maxWordCount = context.GetOrCreateSetting("AutoLLM", "MaxWordCount", 200);
        _parallelCount = context.GetOrCreateSetting("AutoLLM", "ParallelCount", 3);
        _pollingInterval = context.GetOrCreateSetting("AutoLLM", "Interval", 200);
        _halfWidth = context.GetOrCreateSetting("AutoLLM", "HalfWidth", true);
        _maxRetry = context.GetOrCreateSetting("AutoLLM", "MaxRetry", 10);

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
        return text;
    }

    private string UnEscapeSpecialCharacters(string text)
    {
        //将换行进行转义
        text = text.Replace("\\n", "\n");
        text = text.Replace("\\r", "\r");
        return text;
    }

    int curProcessingCount = 0;
    private readonly object _lockObject = new object();

    async Task ProcessTaskBatch(List<TaskData> tasks)
    {
        int hashkey = tasks.GetHashCode();
        try
        {
            Log($"翻译开始Batch:" + hashkey);
            List<string> texts = new List<string>();
            foreach (var task in tasks)
            {
                texts.AddRange(task.texts);
            }
            var system = Config.prompt_base
            .Replace("{{KIND_NOTES}}", Config.prompt_batch_note)
            .Replace("{{KIND_EXAMPLE}}", Config.prompt_batch_example)
            .Replace("{{GAMENAME}}", _gameName)
            .Replace("{{GAMEDESC}}", _gameDesc)
            .Replace("{{OTHER}}", _requirement)
            .Replace("{{HISTORY}}", string.Join("\n", translateDB.Search(texts, 1000)))
            .Replace("{{TARGET_LAN}}", DestinationLanguage)
            .Replace("{{SOURCE_LAN}}", SourceLanguage)
            .Replace("{{RECENT}}", string.Join("\n", recentTranslate));
            var otxt = "{\"texts\":\n[\n";
            int index = 0;
            foreach (var data in texts)
            {
                var t = EscapeSpecialCharacters(data);
                otxt += $"\"{t}\"";
                if (index < texts.Count - 1)
                {
                    otxt += ",\n";
                }
                else
                {
                    otxt += "\n";
                }
                index++;
            }
            otxt += "]\n}";

            var messages = new List<object>
            {
                new { role = "system", content = system },
                new { role = "user", content = otxt }
            };

            var requestBody = new
            {
                model = _model,
                temperature = 0.1,
                max_tokens = 4000,
                top_p = 1,
                frequency_penalty = 0,
                presence_penalty = 0,
                messages
            };

            var requestData = JsonConvert.SerializeObject(requestBody);
            //Log($"翻译请求");
            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.Authorization] = $"Bearer {_apiKey}";
                client.Headers[HttpRequestHeader.ContentType] = "application/json";
                string response = await client.UploadStringTaskAsync(_url, "POST", requestData);
                //Log($"翻译请求成功: {response}");
                // bool lasttry = false;
                // foreach (var task in tasks)
                // {
                //     if (task.retryCount == _maxReTryCount - 1)
                //     {
                //         lasttry = true;
                //         break;
                //     }
                // }
                var ttxts = GetTranslatedText(response);
                if ((ttxts?.Length ?? 0) != texts.Count)
                {
                    Log($"翻译结果数量不匹配: {ttxts?.Length} != {texts.Count}");
                    throw new Exception($"翻译结果数量不匹配: {ttxts?.Length} != {texts.Count}");
                }
                else
                {
                    Log($"翻译成功: {ttxts?.Length} == {texts.Count}");
                    int offset = 0;
                    foreach (var task in tasks)
                    {
                        task.result = ttxts.Skip(offset).Take(task.texts.Length).ToArray();
                        offset += task.texts.Length;
                        for (var i = 0; i < task.texts.Length; i++)
                        {
                            task.result[i] = translateDB.FindTerminology(task.texts[i]) ?? task.result[i];
                        }
                        task.state = TaskData.TaskState.Completed;
                        for (var i = 0; i < task.texts.Length; i++)
                        {
                            lock (recentTranslate)
                            {
                                if (recentTranslate.Count > 10)
                                    recentTranslate.RemoveAt(0);
                                recentTranslate.Add($"{task.texts[i]} === {task.result[i]}");
                            }
                            translateDB.AddData(task.texts[i], task.result[i]);
                        }
                    }

                    translateDB.SortData();

                }
            }

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
                        Log($"重新翻译:" + task.GetHashCode());
                        task.state = TaskData.TaskState.Waiting;
                        task.result = null;
                    }
                    else
                    {
                        Log($"重试翻译依然失败，没救了:" + task.GetHashCode());
                        task.state = TaskData.TaskState.Failed;
                    }
                }
            }
            lock (_lockObject)
                curProcessingCount--;
        }
    }
    async Task ProcessTaskSingle(TaskData task)
    {
        int hashkey = task.GetHashCode();
        try
        {
            Log($"翻译开始Single:" + task.texts[0]);
            var system = Config.prompt_base
            .Replace("{{KIND_NOTES}}", Config.prompt_single_note)
            .Replace("{{KIND_EXAMPLE}}", Config.prompt_single_example)
            .Replace("{{GAMENAME}}", _gameName)
            .Replace("{{GAMEDESC}}", _gameDesc)
            .Replace("{{OTHER}}", _requirement)
            .Replace("{{HISTORY}}", string.Join("\n", translateDB.Search(task.texts.ToList(), 1000)))
            .Replace("{{TARGET_LAN}}", DestinationLanguage)
            .Replace("{{SOURCE_LAN}}", SourceLanguage)
            .Replace("{{RECENT}}", string.Join("\n", recentTranslate));
            var otxt = $"\"{EscapeSpecialCharacters(task.texts[0])}\"";
            var messages = new List<object>
            {
                new { role = "system", content = system },
                new { role = "user", content = otxt }
            };

            var requestBody = new
            {
                model = _model,
                temperature = 0.1,
                max_tokens = 4000,
                top_p = 1,
                frequency_penalty = 0,
                presence_penalty = 0,
                messages
            };

            var requestData = JsonConvert.SerializeObject(requestBody);
            //Log($"翻译请求");
            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.Authorization] = $"Bearer {_apiKey}";
                client.Headers[HttpRequestHeader.ContentType] = "application/json";
                string response = await client.UploadStringTaskAsync(_url, "POST", requestData);
                JObject jsonResponse = JObject.Parse(response);
                var rawString = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;
                rawString = Regex.Replace(rawString, "<think>.*?</think>", "", RegexOptions.Singleline);
                rawString = rawString.Trim();
                if (_halfWidth)
                {
                    //将全角符号转换为半角符号
                    rawString = Regex.Replace(rawString, @"[！＂＃＄％＆＇（）＊＋，－．／０１２３４５６７８９：；＜＝＞？＠［＼］＾＿｀｛｜｝～]", m => ((char)(m.Value[0] - 0xFEE0)).ToString());
                }
                else
                {
                    if (rawString.StartsWith("“"))
                    {
                        rawString = rawString.Substring(1);
                    }
                    if (rawString.EndsWith("”"))
                    {
                        rawString = rawString.Substring(0, rawString.Length - 1);
                    }
                }
                if (rawString.StartsWith("\""))
                {
                    rawString = rawString.Substring(1);
                }
                if (rawString.EndsWith("\""))
                {
                    rawString = rawString.Substring(0, rawString.Length - 1);
                }

                rawString = UnEscapeSpecialCharacters(rawString);
                if (string.IsNullOrEmpty(rawString))
                {
                    Log($"翻译结果错误：{task.texts[0]}");
                    throw new Exception($"翻译结果错误");
                }
                else
                {

                    task.result = [translateDB.FindTerminology(task.texts[0]) ?? rawString];
                    task.state = TaskData.TaskState.Completed;
                    lock (recentTranslate)
                    {
                        if (recentTranslate.Count > 10)
                            recentTranslate.RemoveAt(0);
                        recentTranslate.Add($"{task.texts[0]} === {task.result[0]}");
                    }
                    translateDB.AddData(task.texts[0], task.result[0]);
                    translateDB.SortData();

                }
            }

        }
        finally
        {
            Log($"翻译结束:" + hashkey);
            //失败了重新翻译
            if (task.state != TaskData.TaskState.Completed)
            {
                task.retryCount++;
                if (task.retryCount < _maxRetry)
                {
                    Log($"重新翻译:" + task.GetHashCode());
                    task.state = TaskData.TaskState.Waiting;
                    task.result = null;
                }
                else
                {
                    Log($"重试翻译依然失败，没救了:" + task.GetHashCode());
                    task.state = TaskData.TaskState.Failed;
                }
            }
            lock (_lockObject)
                curProcessingCount--;
        }
    }

    string FixJson(string rawString)
    {
        rawString = Regex.Replace(rawString, "<think>.*?</think>", "", RegexOptions.Singleline);
        if (rawString.StartsWith("```json"))
        {
            rawString = rawString.Substring(7);
        }
        if (rawString.EndsWith("```"))
        {
            rawString = rawString.Substring(0, rawString.Length - 3);
        }
        // 处理可能的 JSON 格式问题
        if (rawString.LastIndexOf(']') > rawString.LastIndexOf("}"))
        {
            //Log($"添加}} {rawString.LastIndexOf(']')}");
            rawString = rawString.Insert(rawString.LastIndexOf(']') + 1, "}");
        }
        //提取json数据：{XXX}
        var match = Regex.Match(rawString, @"\{[\s\S]*\}");
        rawString = match.Success ? match.Value : rawString;
        return rawString;
    }

    string FixJsonSingleLLM(string json)
    {
        try
        {
            var messages = new List<object>
        {
            new { role = "system", content = Config.jsonfix_prompt },
            new { role = "user", content = json }
        };

            var requestBody = new
            {
                model = _model,
                temperature = 0.1,
                max_tokens = 4000,
                top_p = 1,
                frequency_penalty = 0,
                presence_penalty = 0,
                messages
            };

            var requestData = JsonConvert.SerializeObject(requestBody);
            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.Authorization] = $"Bearer {_apiKey}";
                client.Headers[HttpRequestHeader.ContentType] = "application/json";
                string response = client.UploadString(_url, "POST", requestData);
                JObject jsonResponse = JObject.Parse(response);
                var rawString = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;
                Log($"修复JSON结果: {rawString}");
                rawString = FixJson(rawString);
                if (!string.IsNullOrEmpty(rawString))
                {
                    return rawString;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"修复JSON时发生错误: {ex}");
            return json;
        }
        return json;
    }

    private string[] GetTranslatedText(string Response)
    {

        try
        {
            JObject jsonResponse = JObject.Parse(Response);
            var rawString = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(rawString))
            {
                Log("翻译结果为空");
                return new string[0];
            }

            //删除<think></think>标签以及内部的内容
            rawString = FixJson(rawString);
            //Log($"翻译结果: {rawString}");

            JObject jsondata;
            try
            {
                if (_halfWidth)
                {
                    //将全角符号转换为半角符号
                    rawString = Regex.Replace(rawString, @"[！＂＃＄％＆＇（）＊＋，－．／０１２３４５６７８９：；＜＝＞？＠［＼］＾＿｀｛｜｝～]", m => ((char)(m.Value[0] - 0xFEE0)).ToString());
                }
                //Log($"翻译数据: {rawString} ||| {frawString}");
                jsondata = JObject.Parse(rawString);
            }
            catch (JsonReaderException ex)
            {
                Log($"JSON 解析错误: {ex.Message}, 原始内容: {rawString}");
                return new string[0];
            }

            var rss = jsondata["texts"]?.Select(t => UnEscapeSpecialCharacters(t.ToString())).ToArray() ?? new string[0];
            Log($"翻译结果Count: {rss.Length}");
            return rss;
        }
        catch (Exception ex)
        {
            Log($"获取翻译文本时发生错误: {ex}");
            return new string[0];
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
                        if (tasklist.Count == 1 && tasklist[0].retryCount > 2)
                            _ = ProcessTaskSingle(tasklist[0]);
                        else
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