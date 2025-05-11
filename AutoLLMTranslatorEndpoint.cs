using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Endpoints.Www;

internal class LLMTranslatorEndpoint : WwwEndpoint
{

    #region Since all batching and concurrency are handled within TranslatorTask, please do not modify these two parameters.
    public override int MaxTranslationsPerRequest => 1;
    public override int MaxConcurrency => 100;

    #endregion

    public override string Id => "AutoLLMTranslate";

    public override string FriendlyName => "AutoLLM Translate";
    TranslatorTask task = new TranslatorTask();

    void Log(string txt)
    {
        Logger.Log(txt);
    }

    public override void Initialize(IInitializationContext context)
    {
        if (!context.GetOrCreateSetting("AutoLLM", "Debug", false))
        {
            Logger.CloseLogger();
        }

        // Remove artificial delays
        context.SetTranslationDelay(0.1f);
        context.DisableSpamChecks();
        task.Init(context);
    }

    public override void OnCreateRequest(IWwwRequestCreationContext context)
    {
        Log($"翻译请求: {context.GetHashCode()}");
        var requestBody = new
        {
            texts = context.UntranslatedTexts
        };
        context.Complete(new WwwRequestInfo("http://127.0.0.1:20000/", JsonConvert.SerializeObject(requestBody)));
    }

    public override void OnExtractTranslation(IWwwTranslationExtractionContext context)
    {
        var data = context.ResponseData;

        JObject jsonResponse;
        jsonResponse = JObject.Parse(data);
        Log($"翻译结果: {jsonResponse}");
        var rs = jsonResponse["texts"]?.ToObject<string[]>() ?? null;
        if ((rs?.Length ?? 0) == 0)
        {            
            context.Fail("翻译结果为空");
        }
        else
            context.Complete(rs);
    }

}