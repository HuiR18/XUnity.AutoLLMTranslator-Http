using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Endpoints.Http;
using XUnity.AutoTranslator.Plugin.Core.Web;

internal class LLMTranslatorEndpoint : HttpEndpoint
{

    #region Since all batching and concurrency are handled within TranslatorTask, please do not modify these two parameters.
    public override int MaxTranslationsPerRequest => 1;
    public override int MaxConcurrency => 100;

    #endregion

    public override string Id => "AutoLLMTranslate";

    public override string FriendlyName => "AutoLLM Translate";
    TranslatorTask task = new TranslatorTask();

    public override void Initialize(IInitializationContext context)
    {
        try
        {
            var debuglvl = context.GetOrCreateSetting<Logger.LogLevel>("AutoLLM", "LogLevel", Logger.LogLevel.Error);
            var log2file = context.GetOrCreateSetting<bool>("AutoLLM", "Log2File", false);
            Logger.InitLogger(debuglvl, log2file);
        }
        catch (Exception ex)
        {            
            Logger.InitLogger(Logger.LogLevel.Error, false);
            Logger.Error($"{ex}");
        }
        context.SetTranslationDelay(0.1f);
        task.Init(context);
    }

    public override void OnCreateRequest(IHttpRequestCreationContext context)
    {
        Logger.Debug($"翻译请求: {context.UntranslatedTexts[0]}");
        var requestBody = new
        {
            texts = context.UntranslatedTexts
        };
        context.Complete(new XUnityWebRequest(
            "POST", 
            "http://127.0.0.1:20000/", 
            JsonConvert.SerializeObject(requestBody)
        ));
    }

    public override void OnExtractTranslation(IHttpTranslationExtractionContext context)
    {
        var data = context.Response.Data;

        JObject jsonResponse;
        jsonResponse = JObject.Parse(data);
        Logger.Debug($"翻译结果: {jsonResponse}");
        var rs = jsonResponse["texts"]?.ToObject<string[]>() ?? null;
        if ((rs?.Length ?? 0) == 0)
        {
            context.Fail("翻译结果为空");
        }
        else
            context.Complete(rs);
    }

}