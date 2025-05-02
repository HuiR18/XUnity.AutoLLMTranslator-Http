using XUnity.Common.Logging;

public static class Logger
{
  static System.IO.StreamWriter _logger = null;
  static bool _isDebug = true;

  static void InitLogger()
  {
    if (!_isDebug) return;
    if (_logger == null)
    {
      string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
      var logfile = appDirectory + $"\\AutoLLM.log";
      _logger = new System.IO.StreamWriter(logfile, true);
      _logger.AutoFlush = true;
    }
  }

  public static void CloseLogger()
  {
    _isDebug = false;
  }

  public static void Log(string message)
  {
    if (!_isDebug) return;
    InitLogger();
    message = $"[{DateTime.Now:HH:mm:ss}] {message}";
    XuaLogger.AutoTranslator.Info($"[LLMT]: {message}");
    _logger.WriteLine(message);
  }

}