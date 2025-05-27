using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using FuzzyString;
using System.Collections.Generic;
using System;
using System.IO;
public class TranslateDB
{

  List<string[]> translateDatas = new List<string[]>();
  List<int> existskeys = new List<int>();


  Dictionary<int, string> terminology = new Dictionary<int, string>();

  FuzzyString.FuzzyStringComparisonOptions[] options = new FuzzyStringComparisonOptions[] {
        FuzzyStringComparisonOptions.UseOverlapCoefficient,
        FuzzyStringComparisonOptions.UseLongestCommonSubsequence,
        FuzzyStringComparisonOptions.UseLongestCommonSubstring
    };

  public void Init(IInitializationContext context, string terminology)
  {
    InitDB(context, terminology);
  }

  public string? FindTerminology(string str)
  {
    var hashkey = str.GetHashCode();
    if (terminology.TryGetValue(hashkey, out string value))
    {
      return value;
    }
    return null;
  }

  private string EscapeSpecialCharacters(string text)
  {
    //将换行进行转义
    text = text.Replace("\n", "\\n");
    text = text.Replace("\r", "\\r");
    return text;
  }

  public bool AddData(string key, string value)
  {
    if (key.Length > 100)
      return false;
    var hashkey = key.GetHashCode();
    if (existskeys.Contains(hashkey))
      return false;
    lock (translateDatas)
    {
      //Log($"添加翻译: {parts[0]} = {parts[1]}");
      translateDatas.Add(new string[] { key, value });
      existskeys.Add(hashkey);
    }
    return true;
  }

  static List<string> dataPaths = new List<string>()
  {
    "\\AutoTranslator\\Translation\\{0}\\Text\\",//ReiPatcher,MelonLoader 
    "\\BepInEx\\Translation\\{0}\\Text\\",//BepInEx
  };

  void InitDB(IInitializationContext context, string _terminology)
  {

    //输出当前目录
    string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
    var dir = "";
    foreach (var path in dataPaths)
    {
      var p = appDirectory + string.Format(path,context.DestinationLanguage);
      if (Directory.Exists(p))
      {
        dir = p;
        break;
      }
    }
    //遍历dir目录下所有txt
    if (string.IsNullOrEmpty(dir))
    {
      Logger.Error("没有找到翻译目录");
      return;
    }
    Logger.Info("翻译目录:" + dir);
    //获取当前目录下所有txt文件
    var files = Directory.GetFiles(dir, "*.txt", SearchOption.AllDirectories);

    Logger.Info("初始化数据库");
    foreach (var txtFile in files)
    {      
      //读取文件内容
      string[] lines = File.ReadAllLines(txtFile);
      Logger.Info($"读取文件：{txtFile} 行数：{lines.Length}");
      //遍历每一行
      foreach (string line in lines)
      {
        if (string.IsNullOrEmpty(line))
          continue;
        //分割字符串
        // Split by the first occurrence of "=" that is not preceded by "\" (escaped)
        int equalIndex = -1;
        bool foundUnescaped = false;
        for (int i = 0; i < line.Length; i++)
        {
          if (line[i] == '=' && (i == 0 || line[i - 1] != '\\'))
          {
            equalIndex = i;
            foundUnescaped = true;
            break;
          }
        }

        string[] parts = foundUnescaped
            ? new string[] { line.Substring(0, equalIndex), line.Substring(equalIndex + 1) }
            : new string[] { line };
        if (parts.Length == 2)
        {
          if (parts[0].Length > 100)
            continue;
          var hashkey = parts[0].GetHashCode();
          if (existskeys.Contains(hashkey))
          {
            continue;
          }
          //Log($"添加翻译: {parts[0]} = {parts[1]}");
          translateDatas.Add(new string[] { parts[0], parts[1] });
          existskeys.Add(hashkey);
        }
      }
    }
    if (!string.IsNullOrEmpty(_terminology))
    {
      var txts = _terminology.Split('|');
      foreach (var txt in txts)
      {
        var ts = txt.Split(new string[] { "==" }, StringSplitOptions.RemoveEmptyEntries);
        if (ts.Length == 2)
        {
          var hashkey = ts[0].GetHashCode();
          if (existskeys.Contains(hashkey))
          {
            continue;
          }
          Logger.Debug($"添加词表: {ts[0]} = {ts[1]}");
          terminology.Add(hashkey, ts[1]);
          translateDatas.Add(new string[] { ts[0], ts[1] });
          existskeys.Add(hashkey);
        }
        else
        {
          Logger.Error($"格式错误: {txt}");
        }

      }
    }

    SortData();
    Logger.Info("初始化数据库完成");
  }

  public void SortData()
  {
    lock (translateDatas)
    {
      translateDatas.Sort((a, b) => a[0].Length.CompareTo(b[0].Length));
    }
  }

  public List<string> Search(List<string> keys, int Length = 2000)
  {
    var rs = new List<string>();
    int l = 0;
    List<string> findkeys = new List<string>();
    lock (translateDatas)
    {
      foreach (var kvp in translateDatas)
      {
        //Log($"{kvp.Key} in {key} == {key.LevenshteinDistance(kvp.Key)}");
        foreach (var key in keys)
        {
          if (key.ApproximatelyEquals(kvp[0], FuzzyStringComparisonTolerance.Strong, options))
          {
            if (!findkeys.Contains(kvp[0]))
            {
              l += kvp[0].Length + kvp[1].Length;
              if (l > Length)
                break;
              findkeys.Add(kvp[0]);
              rs.Add($"{kvp[0]} === {EscapeSpecialCharacters(kvp[1])}");
              Logger.Debug($"找到翻译: {kvp[0]} = {kvp[1]}");
            }
          }
        }
      }
    }
    return rs;
  }
}