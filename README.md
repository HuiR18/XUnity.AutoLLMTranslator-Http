# XUnity.AutoLLMTranslator

## 概述  
## Overview
XUnity.AutoLLMTranslator 是一个用于 XUnity.AutoTranslator 框架的插件，它通过大型语言模型（LLM）实现游戏文本翻译。  
XUnity.AutoLLMTranslator is a plugin for the XUnity.AutoTranslator framework that enables game text translation using large language models (LLM).  

该插件提供了高性能、可定制的翻译功能。  
This plugin offers high-performance and customizable translation features.  

## 特性  
## Features
- **支持远程api及本地服务器**  
  **Supports remote APIs and local servers**  
- **使用了完全独立于AutoTranslator的批处理机制实现了高效的翻译功能**  
  **Implements an efficient translation mechanism independent of AutoTranslator's batch processing**  
- **实现了大量容错和兼容机制，完美支持各种尺寸的大模型**  
  **Incorporates extensive fault tolerance and compatibility mechanisms, fully supporting large models of various sizes**  
- **实现了简单的上下文机制，让翻译尽可能的统一语境和术语**  
  **Introduces a simple context mechanism to unify translation context and terminology as much as possible**  
- **支持自定义术语等功能，实现更个性化的翻译**  
  **Supports custom terminology and other features for more personalized translations**  

## 安装  
## Installation
1. 在游戏中安装 XUnity.AutoTranslator(https://github.com/bbepis/XUnity.AutoTranslator) 框架。  
   Install the XUnity.AutoTranslator framework (https://github.com/bbepis/XUnity.AutoTranslator) in the game.  
2. 将 `XUnity.AutoLLMTranslator.dll` 文件复制到的插件文件夹中:  
   Copy the `XUnity.AutoLLMTranslator.dll` file to the plugin folder:  
   - ReiPatcher位于<GameDir>/<GameName>_Data/Managed/Translators  
     ReiPatcher is located at <GameDir>/<GameName>_Data/Managed/Translators  
   - BepinEx位于<GameDir>/BepinEx/plugins/XUnity.AutoTranslator/Translators  
     BepinEx is located at <GameDir>/BepinEx/plugins/XUnity.AutoTranslator/Translators  
3. 配置 `Config.ini` 文件以使用 "AutoLLMTranslate" 端点。  
   Configure the `Config.ini` file to use the "AutoLLMTranslate" endpoint.  

## 配置  
### Config.ini  
在 `Config.ini` 文件中修改以下配置：  
Modify the following configuration in the `Config.ini` file:  
```
    [Service]
    Endpoint=AutoLLMTranslate
```
同时需要添加以下配置：  
Additionally, add the following configurations:  
- [AutoLLM]：配置头  Configuration header  
- *`Model`：用于翻译的模型。  The model used for translation.  
- *`URL`：LLM 服务器的 URL，一般以/v1结尾。也可以是/chat/completions的完整路径。  URL of the LLM server, usually ending with `/v1`. It can also be the full path to `/chat/completions`.  
- `APIKey`：LLM 服务器的 API 密钥。如果使用本地模型，可以留空。 API key for the LLM server. If using a local model, this can be left blank.
- `Requirement`：额外的翻译需求或指令，例如:使用莎士比亚的风格进行翻译。  Additional translation requirements or instructions, e.g., translating in Shakespearean style.   
- `Terminology`：术语表，使用|隔开不同术语，使用==连接原文和翻译。例如：Lorien==罗林|Skadi==斯卡蒂  。Terminology list, with different terms separated by `|` and original text and translation connected by `==`,e.g.,Lorien==罗林|Skadi==斯卡蒂.    
- `GameName`: 游戏名字  Name of the game  
- `GameDesc`：游戏介绍，用于帮助AI进行更准确的翻译，可以对游戏的玩法/类型/风格进行描述。 Game description to help the AI perform more accurate translations. It can describe gameplay, type, or style.    
- `ModelParams`: 模型参数定制，使用json格式书写，会直接传递给模型api。例如：{"temperature":0.1}  
  Model parameter customization, written in JSON format, will be directly passed to the model API. For example: {"temperature":0.1}  
- `MaxWordCount`：每批翻译的最大单词数，适当的单词可以减少并发数量从而提高翻译速度。  Maximum number of words per batch translation. Proper word count can reduce concurrency and improve translation speed. 
- `ParallelCount`：并行翻译任务的最大数量，一般由LLM的提供商决定。  Maximum number of parallel translation tasks, usually determined by the LLM provider.  
- `Interval`：轮询间隔（毫秒）,每次翻译的间隔，在间隔中系统会尽可能的合并翻译内容，以便提高翻译速度减少并发，但太长会导致响应不够及时。   Polling interval (in milliseconds). During this interval, the system will try to merge translation content to improve speed and reduce concurrency. However, too long an interval may lead to delayed responses.  
- `HalfWidth`：是否将全角字符转换为半角，在字体无法显示全角符号的时候使用这个。  Whether to convert full-width characters to half-width. Use this when fonts cannot display full-width symbols.  
- `MaxRetry`：失败翻译的最大重试次数，一般不动，如果大模型失败率太高，可以尝试提高。  Maximum retry attempts for failed translations. Generally, this should not be changed, but if the large model has a high failure rate, you can try increasing it.  
- `Debug`：启用或禁用调试日志(AutoLLM.log)。  Enable or disable debug logs(AutoLLM.log).  

** *为必填参数 **  
** *Required parameters **  

此外，你需要正确的配置：  
Additionally, you need to configure correctly:  
```
Language=zh-cn
FromLanguage=en
```
### 范例  
### Example

```
[Service]
Endpoint=AutoLLMTranslate

[General]
Language=zh_cn
FromLanguage=en
```

完整配置complete configuration:

```
[AutoLLM]
APIKey= <KEY>  
Model=qwen-turbo  
URL=https://dashscope.aliyuncs.com/compatible-mode/v1  
Requirement=/no_think  
Terminology=
GameName=DeathMustDie  
GameDesc=一个刷装备打怪的游戏、暗黑破坏神的风格和元素  
ModelParams={"temperature":0.1}
HalfWidth=True  
MaxWordCount=200  
ParallelCount=3  
Interval=200  
Debug=False  
MaxRetry=10  
```

最小配置minimum configuration:

```
[AutoLLM]
Model=qwen3:4b
URL=http://localhost:11434/v1  
```

## 本地 LLM 服务器  
## Local LLM Server  
除了使用LLM远程服务以外，也可以使用类似ollama的本地服务，  
In addition to using LLM remote services, you can also use local services like ollama,  
只需要填写Model和URL即可  
Just fill in the Model and URL  


## 关于LLM大模型建议  
## Recommendations for Large LLM Models  
在选择大模型时往往需要同时权衡速度和质量，经过我的测试，有以下建议：  
When choosing a large model, you often need to balance speed and quality. Based on my tests, I have the following recommendations:  
- 推荐尺寸：8b，质量和速度都可以接受，本地也可以运行，例如qwen3:8bQ4是可以的。  
  Recommended size: 8b, with acceptable quality and speed, and can run locally, e.g., qwen3:8bQ4.  
- 最低尺寸：4b，也能比较好的完成任务，但是比8b质量大幅度下降并伴随较多的错误。  
  Minimum size: 4b, which can also complete tasks well, but with significantly lower quality and more errors compared to 8b.  
当然，因为插件提供了比较强大的容错能力，使用更低的模型也是可以的(我甚至测试了qwen3:0.6b)，但是质量和错误率会比较糟糕。  
Of course, because the plugin provides strong fault tolerance, using lower models is also possible (I even tested qwen3:0.6b), but the quality and error rate will be worse.  
如果本地设备较差，还是推荐网络服务，比如qwen-turbo非常便宜，很多厂商甚至有免费的小模型可以使用。  
If the local device is poor, it is still recommended to use network services, such as qwen-turbo, which is very cheap, and many vendors even offer free small models.  

## 如何获得模型  
## How to Obtain Models  
### 免费的方式  
### Free Methods  
- openroute、siliconflow等地方都提供了免费的模型可以使用  
  Openroute and Siliconflow provide free models for use.  
- 阿里云等云服务商应该了提供了大量的免费额度  
  Cloud service providers like Alibaba Cloud offer generous free quotas.  
- 通过ollama等工具进行本地部署  
  Deploy locally using tools like Ollama.
### 付费的方式
- 去模型提供商付费  
  Pay the model provider.

## 可能的问题  
## Possible Issues  
- 无法翻译/翻译异常：  
  Unable to translate/translation error:  
    0. 检查你的 AutoTranslator 是否正确运行，AutoTranslator 目前并不支持 IL2CPP 类型游戏的插件运行。  
       Verify that your AutoTranslator is functioning correctly. Currently, AutoTranslator does not support plugin operation for IL2CPP games.   
    1. 请检查你的LLM服务配置是否正确且生效。  
       Please check if your LLM service configuration is correct and effective.  
    2. 确保20000端口没有被占用，可以在游戏运行的情况下使用浏览器访问 http://localhost:20000 确认。  
       Ensure that port 20000 is not occupied. You can confirm this by accessing http://localhost:20000 in a browser while the game is running.  
    3. 是否使用了足够强大的模型。  
       Check if a sufficiently powerful model is being used.  
    4. 缺少Newtonsoft.Json.dll或者Newtonsoft.Json.dll不兼容。
      - 下载这个文件放到Managed目录下：[Newtonsoft.Json.dll](https://github.com/NothingNullNull/XUnity.AutoLLMTranslator/releases/download/2025%2F5%2F3/Newtonsoft.Json.dll)
    5. LLM的URL和模型名字是否填写正确：  
       Check if the LLM URL and model name are correctly filled in:  
       - 以下URL是正确的：  
         The following URLs are correct:  
         http(s)://XXXXXXX/v1  
         http(s)://XXXXXXX/v1/chat/completions  
       - 以下URL是错误的：  
         The following URLs are incorrect:  
         http(s)://XXXXXXX/v3  
         http(s)://XXXXXXX/  
- 翻译很慢：  
  Translation is slow:  
    1.是否使用了过于巨大的模型，我的建议是8b。  
      Check if an overly large model is being used; my recommendation is 8b.  
    2.是否已经触发了LLM供应商的限制，例如QPM、TPM等。这种时候就需要适当调整Config的相关配置。  
      Check if the LLM provider's limits, such as QPM, TPM, etc., have been triggered. In this case, you need to adjust the relevant Config settings.  
    3.本地LLM服务器是否有足够的硬件支持。  
      Ensure that the local LLM server has sufficient hardware support.  
    4.使用了think模型，大部分情况下都不需要用到think，在qwen3下可以使用/no_think来进行关闭。  
      Using the think model is generally unnecessary in most cases. For qwen3, you can disable it using /no_think.
- 插件被关闭：  
  Plugin is disabled:  
    虽然插件会尽力避免失败，但是出现太多失败后AutoTranslator会自动关闭插件,这个时候需要重启游戏/插件。  
    Although the plugin will try to avoid failures, if too many failures occur, AutoTranslator will automatically disable the plugin. 

## 许可证  
## License  
本项目根据包含的许可证文件中规定的条款授权。  
This project is licensed under the terms specified in the included license file.  

## 致谢  
## Acknowledgements  
该插件基于 XUnity.AutoTranslator 开发。  
This plugin is developed based on XUnity.AutoTranslator.  
同时也使用了FuzzyString(https://github.com/kdjones/fuzzystring)来实现文本搜索。  
It also uses FuzzyString (https://github.com/kdjones/fuzzystring) for text search.  
感谢他们的付出  
Thanks to them for their contributions.