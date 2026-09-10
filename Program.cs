// GufoFAQ 前台問答 API 範例（對應 README.md）
//
// 涵蓋以 X-Widget-Token 認證的五支端點：
//   POST /api/public/faq                非串流問答（一次回完整 JSON）
//   POST /api/public/ask                串流問答（SSE，一步）
//   POST /api/public/rating/{log_ref}   評分
//   POST /api/public/share              分享連結
//   GET  /api/public/welcome            開場白
//
// 跑法：dotnet run -- <指令>
//   faq / ask / chat / rating / share / welcome / all
//
// 設定：改下面三個常數，或設環境變數
//   GUFOFAQ_BASE_URL、GUFOFAQ_WIDGET_TOKEN、GUFOFAQ_ORIGIN（環境變數優先）。

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

const string BaseUrlDefault = "https://<你的 GufoFAQ 網域>";
const string WidgetTokenDefault = "wgt_你的權杖";
const string OriginDefault = "https://你登記的網域";

var baseUrl = (Environment.GetEnvironmentVariable("GUFOFAQ_BASE_URL") ?? BaseUrlDefault).TrimEnd('/');
var widgetToken = Environment.GetEnvironmentVariable("GUFOFAQ_WIDGET_TOKEN") ?? WidgetTokenDefault;
var origin = (Environment.GetEnvironmentVariable("GUFOFAQ_ORIGIN") ?? OriginDefault).TrimEnd('/');

if (baseUrl.Contains('<') || widgetToken.StartsWith("wgt_你的") || origin.Contains('你'))
{
    Console.Error.WriteLine("請先設定 GUFOFAQ_BASE_URL、GUFOFAQ_WIDGET_TOKEN、GUFOFAQ_ORIGIN"
                            + "（或改 Program.cs 最上面的常數）。");
    return 1;
}

// 只有在對「自簽憑證的測試站」呼叫時才需要；正式環境不要開。
var handler = new HttpClientHandler();
if (Environment.GetEnvironmentVariable("GUFOFAQ_INSECURE_TLS") == "1")
    handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
client.DefaultRequestHeaders.Add("X-Widget-Token", widgetToken);
// ⚠️ 從後端呼叫時 `Origin` 要自己帶——瀏覽器會自動帶，HttpClient 不會。
// 沒有 Origin 也沒有 Referer 時，請求會被判成「來源不允許」而 403。
client.DefaultRequestHeaders.Add("Origin", origin);
// 錯誤訊息的語言（值域 zh-TW / en）。不帶就用租戶自己的設定。
client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("zh-TW"));

var command = args.Length > 0 ? args[0] : "all";

try
{
    switch (command)
    {
        case "faq": await DemoFaq(); break;
        case "ask": await DemoAsk(); break;
        case "chat": await DemoMultiTurn(); break;
        case "rating": await DemoRating(); break;
        case "share": await DemoShare(); break;
        case "welcome": await DemoWelcome(); break;
        case "all":
            await DemoWelcome();
            await DemoFaq();
            await DemoAsk();
            await DemoMultiTurn();
            await DemoRating();
            await DemoShare();
            break;
        default:
            Console.Error.WriteLine($"不認得的指令：{command}");
            Console.Error.WriteLine("可用：faq / ask / chat / rating / share / welcome / all");
            return 1;
    }
}
catch (ApiException e)
{
    // 分流一律看狀態碼與 code，不要比對訊息字面（訊息會隨語言變）。
    Console.Error.WriteLine($"\n呼叫失敗：HTTP {(int)e.Status} code={e.Code ?? "(無)"} 關聯編號={e.CorrelationId ?? "(無)"}");
    Console.Error.WriteLine($"  {ForDisplay(e.Message)}");
    Console.Error.WriteLine("  " + Advice(e));
    if (e.RetryAfter is not null)
        Console.Error.WriteLine($"  Retry-After: {e.RetryAfter} 秒後可再試");
    return 2;
}

return 0;

// ── 一、開場白 ────────────────────────────────────────────────────────────────

async Task DemoWelcome()
{
    Console.WriteLine("== 開場白 ==");
    var w = await GetJson<WelcomeResult>("/api/public/welcome");
    Console.WriteLine($"  {w.Content}");
}

// ── 二、非串流問答 ────────────────────────────────────────────────────────────

async Task DemoFaq()
{
    Console.WriteLine("== 非串流問答 ==");
    var r = await Faq("你們的營業時間？");
    Console.WriteLine($"  答案：{r.Answer}");
    PrintSources(r.Sources);
    Console.WriteLine($"  log_ref={r.LogRef}");
    Console.WriteLine($"  room_ref={r.RoomRef}（下一輪帶回去就是接續同一段對話）");
}

// ── 三、串流問答（SSE）───────────────────────────────────────────────────────

async Task DemoAsk()
{
    Console.WriteLine("== 串流問答 ==");
    var result = await Ask("請簡單說明你們的退貨流程", onDelta: Console.Write);
    Console.WriteLine();
    Console.WriteLine($"  （完整答案 {result.Answer.Length} 字）");
    PrintSources(result.Sources);
    Console.WriteLine($"  log_ref={result.LogRef}  room_ref={result.RoomRef}");
    if (result.AnswerSource == "qa_direct")
        Console.WriteLine("  這一則是 QA 集裡的逐字答案（不是模型生成）");
    if (result.SuggestQuestions.Count > 0)
        Console.WriteLine($"  建議追問：{string.Join(" / ", result.SuggestQuestions)}");
}

// ── 四、多輪續談 ──────────────────────────────────────────────────────────────

async Task DemoMultiTurn()
{
    Console.WriteLine("== 多輪續談 ==");
    var first = await Faq("你們有哪些付款方式？");
    Console.WriteLine($"  第一輪：{Trim(first.Answer)}");

    // 把上一輪的 room_ref 帶回去；每一輪都會回一個新的，下一輪用最新那個。
    var second = await Faq("那第一種可以分期嗎？", roomRef: first.RoomRef);
    Console.WriteLine($"  第二輪：{Trim(second.Answer)}");
    Console.WriteLine("  ↑ 第二輪的問題沒有主詞，答得出來就代表前文接上了");
}

// ── 五、評分 ──────────────────────────────────────────────────────────────────

async Task DemoRating()
{
    Console.WriteLine("== 評分 ==");
    var r = await Faq("運費怎麼算？");
    Console.WriteLine($"  先問一題，log_ref={r.LogRef}");

    // rating 只收 "up"／"down"；不評分就不要呼叫（沒有「未評分」這個值）。
    // 路徑上的 log_ref 只在自己租戶下有效；格式錯／不是你的／不存在一律 404。
    await PostJson<JObject>($"/api/public/rating/{Uri.EscapeDataString(r.LogRef)}", new
    {
        rating = "up",
        feedback = "回答很有幫助",
    });
    Console.WriteLine("  已送出好評");
}

// ── 六、分享 ──────────────────────────────────────────────────────────────────

async Task DemoShare()
{
    Console.WriteLine("== 分享 ==");
    var r = await Faq("如何聯絡客服？");
    var s = await PostJson<ShareResult>("/api/public/share", new { log_ref = r.LogRef });
    Console.WriteLine($"  分享連結：{baseUrl}/shared/{s.Token}");
    Console.WriteLine("  （同一則再分享會拿到同一個 token；這一支需要租戶開通「問答紀錄」功能）");
}

// ── 端點呼叫 ──────────────────────────────────────────────────────────────────

/// <summary>非串流問答。`roomRef` 帶上一輪回的值＝接續對話，`null`＝開新對話。</summary>
async Task<FaqResult> Faq(string query, string? roomRef = null)
    => await PostJson<FaqResult>("/api/public/faq", new { query, room_ref = roomRef });

/// <summary>
/// 串流問答。一次 POST 就開始串流（不需要先取號再另開 GET）。
///
/// 每一筆記錄是 `data: {json}` ＋空行；答案＝依序串接所有 `chunk_type=="message"` 的 `content`。
/// 串流結束就是回應結束，**沒有哨兵字串**。
/// </summary>
async Task<AskResult> Ask(string query, Action<string>? onDelta = null, string? roomRef = null)
{
    using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/public/ask")
    {
        Content = JsonBody(new { query, room_ref = roomRef }),
    };
    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

    // ResponseHeadersRead：不要等整個 body 收完才回來，否則就沒有串流可言。
    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
    var correlationId = CorrelationIdOf(resp);
    if (!resp.IsSuccessStatusCode)
    {
        // 開始串流**之前**失敗：狀態碼還在，照一般錯誤處理。
        var body = await resp.Content.ReadAsStringAsync();
        throw ApiException.From(resp.StatusCode, body, correlationId, resp.Headers.RetryAfter?.Delta?.TotalSeconds);
    }

    var result = new AskResult();
    var answer = new StringBuilder();

    using var stream = await resp.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream, Encoding.UTF8);
    while (await reader.ReadLineAsync() is { } line)
    {
        // 空行是記錄邊界；本服務一筆記錄只有一行 `data:`，所以逐行處理就夠。
        if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
        var payload = line.Substring(5).Trim();
        if (payload.Length == 0) continue;

        JObject chunk;
        try { chunk = JObject.Parse(payload); }
        catch (JsonException) { continue; }   // 壞記錄跳過，不要讓整條串流死掉

        var type = chunk.Value<string>("chunk_type");
        switch (type)
        {
            case "message":
                var delta = chunk.Value<string>("content") ?? "";
                answer.Append(delta);
                onDelta?.Invoke(delta);
                break;

            case "chat_room":
                var data = chunk["data"] as JObject;
                if (data is null) break;
                result.LogRef = data.Value<string>("log_ref");
                result.RoomRef = data.Value<string>("room_ref");
                result.AnswerSource = data.Value<string>("answer_source");
                result.LatestChatLogId = data.Value<long?>("latest_chat_log_id");
                if (data["search_results"] is JArray hits) result.Sources = hits;
                if (data["suggest_questions"] is JArray sq)
                    result.SuggestQuestions = sq.Select(t => t.ToString()).ToList();
                break;

            case "error":
                // **開始串流之後**才失敗：狀態碼早就送出去了，錯誤只出現在這裡。
                // SSE 的呼叫端兩種都要處理。
                result.Error = chunk.Value<string>("content");
                break;

            case "thinking":
            case "status":
            case "end":
            case "agent_tool_call":
            case "agent_tool_result":
                break;   // 中繼資料，這支範例不用

            default:
                // 不認得的型別一律忽略、不要當成錯誤（之後可能會多出新的）。
                break;
        }
    }

    result.Answer = answer.ToString();
    if (result.Error is not null)
        Console.Error.WriteLine($"\n  ⚠ 串流中的錯誤：{result.Error}");
    return result;
}

// ── 共用 ──────────────────────────────────────────────────────────────────────

void PrintSources(IEnumerable<JToken>? sources)
{
    var list = sources?.ToList() ?? new List<JToken>();
    Console.WriteLine($"  來源 {list.Count} 筆：");
    foreach (var s in list)
    {
        // 逐筆的欄位由引擎給，讀法是「有就用、沒有就跳過」。
        // 保證有的只有 source_no；保證沒有的是 index，以及 document 裡的 privilege。
        var no = s.Value<int?>("source_no");
        var name = s.Value<string>("doc_name") ?? s.Value<string>("data_source") ?? "(未命名)";
        var score = s.Value<double?>("score");
        Console.Write($"    [[{no?.ToString() ?? "?"}]] {name}");
        if (score is not null) Console.Write($"（相似度 {score:0.00}）");
        // `document` 只有 /api/public/faq 會給（SSE 那一支不送）。
        if (s["document"] is JObject doc)
        {
            var title = doc.Value<string>("title");
            if (!string.IsNullOrWhiteSpace(title)) Console.Write($" — {title}");
        }
        Console.WriteLine();
    }
}

static string Trim(string s) => s.Length <= 60 ? s : s.Substring(0, 60) + "…";

/// <summary>錯誤訊息印到終端機用：非 JSON 的回應（例如打錯網域時的整頁 HTML）截短。</summary>
static string ForDisplay(string message) =>
    message.Length <= 300 ? message : message.Substring(0, 300) + $"…（共 {message.Length} 字）";

/// <summary>三種「答不出來」的處置完全不同，所以要照 code 分流。</summary>
static string Advice(ApiException e) => e.Code switch
{
    "quota_exceeded" => "問答額度用完了，請聯絡我們加額度。",
    "rate_limited" => "每分鐘請求數超限，照 Retry-After 等一下再送。",
    "upstream_rate_limited" => "引擎端當日用量已滿，退避後重試。",
    "upstream_rejected" => "這個請求引擎不受理（例如對話參照已失效）——重試不會好，改成開新對話。",
    "no indexes" => "這個租戶還沒有任何資料集，先把資料匯進去。",
    "chat_error" => "跨服務故障，請附關聯編號回報；不要無限重試。",
    "upstream_unavailable" => "引擎暫時不可用，照 Retry-After 重試。",
    "subscription_expired" or "account_frozen" or "disclaimer_required" => "租戶狀態的問題，請聯絡我們。",
    _ => e.Status switch
    {
        HttpStatusCode.Unauthorized => "權杖缺漏、錯誤或已撤銷。",
        HttpStatusCode.Forbidden => "檢查 Origin 是不是登記過的網域，以及租戶功能是否開通。",
        HttpStatusCode.NotFound => "評分／分享那兩支：log_ref 無效、不是這個租戶的、或那一則不存在。\n     其餘端點回 404 多半是基底網址或路徑不對。",
        HttpStatusCode.UnprocessableEntity => "body 形狀不合：query 不可為空、也不要多送未知欄位。",
        _ => "照 HTTP 狀態碼與 code 分流，不要比對訊息字面。",
    },
};

StringContent JsonBody(object body)
{
    // NullValueHandling.Ignore：room_ref 沒有值時不要送 null 過去。
    // ⚠️ 這兩支端點**拒收未知欄位**（多送一個就 422），所以不要順手加自訂欄位。
    var json = JsonConvert.SerializeObject(body, new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
    });
    return new StringContent(json, Encoding.UTF8, "application/json");
}

Task<T> GetJson<T>(string path) => SendJson<T>(HttpMethod.Get, path, null);
Task<T> PostJson<T>(string path, object body) => SendJson<T>(HttpMethod.Post, path, body);

async Task<T> SendJson<T>(HttpMethod method, string path, object? body)
{
    using var req = new HttpRequestMessage(method, baseUrl + path);
    if (body is not null) req.Content = JsonBody(body);

    using var resp = await client.SendAsync(req);
    var text = await resp.Content.ReadAsStringAsync();
    var correlationId = CorrelationIdOf(resp);

    if (!resp.IsSuccessStatusCode)
        throw ApiException.From(resp.StatusCode, text, correlationId, resp.Headers.RetryAfter?.Delta?.TotalSeconds);

    var parsed = JsonConvert.DeserializeObject<T>(text);
    if (parsed is null) throw new ApiException(resp.StatusCode, "回應不是預期的 JSON 形狀", null, correlationId, null);
    return parsed;
}

static string? CorrelationIdOf(HttpResponseMessage resp)
    => resp.Headers.TryGetValues("X-Correlation-ID", out var v) ? v.FirstOrDefault() : null;

// ── 資料結構 ──────────────────────────────────────────────────────────────────

/// <summary>`POST /api/public/faq` 的回應。</summary>
public class FaqResult
{
    [JsonProperty("answer")] public string Answer { get; set; } = "";
    /// <summary>這一則答案的來源。**只有這一支會帶 `document`**（SSE 那一支不送）。</summary>
    [JsonProperty("sources")] public JArray Sources { get; set; } = new();
    /// <summary>評分與分享都用它。是綁租戶的簽章字串，不要自己組。</summary>
    [JsonProperty("log_ref")] public string LogRef { get; set; } = "";
    /// <summary>下一輪帶回去＝接續同一段對話。每一輪都會回一個新的。</summary>
    [JsonProperty("room_ref")] public string? RoomRef { get; set; }
}

/// <summary>`POST /api/public/ask` 串流收完之後整理出來的結果。</summary>
public class AskResult
{
    public string Answer { get; set; } = "";
    public JArray Sources { get; set; } = new();
    public string? LogRef { get; set; }
    public string? RoomRef { get; set; }
    /// <summary>`"qa_direct"` ＝ QA 集裡的逐字答案；`null` ＝模型生成。</summary>
    public string? AnswerSource { get; set; }
    /// <summary>顯示用編號。**不能拿去評分或分享**，那兩支只認 `log_ref`。</summary>
    public long? LatestChatLogId { get; set; }
    public List<string> SuggestQuestions { get; set; } = new();
    /// <summary>串流中出現的錯誤（`chunk_type=="error"`）。</summary>
    public string? Error { get; set; }
}

public class ShareResult
{
    [JsonProperty("token")] public string Token { get; set; } = "";
}

public class WelcomeResult
{
    [JsonProperty("content")] public string Content { get; set; } = "";
}

/// <summary>
/// 失敗回應。三種形狀：`{"detail": "句子"}`、`{"detail": {"code", …}}`、`{"error", "code"}`。
/// 分流一律用 HTTP 狀態碼 ＋ `code`，不要比對句子（句子會隨 Accept-Language 變）。
/// </summary>
public class ApiException : Exception
{
    public HttpStatusCode Status { get; }
    public string? Code { get; }
    public string? CorrelationId { get; }
    public double? RetryAfter { get; }

    public ApiException(HttpStatusCode status, string message, string? code, string? correlationId, double? retryAfter)
        : base(message)
    {
        Status = status;
        Code = code;
        CorrelationId = correlationId;
        RetryAfter = retryAfter;
    }

    public static ApiException From(HttpStatusCode status, string body, string? correlationId, double? retryAfter)
    {
        string? code = null;
        var message = body;
        try
        {
            var root = JObject.Parse(body);
            if (root["detail"] is JObject o)
            {
                code = o.Value<string>("code");
                message = o.Value<string>("detail") ?? o.ToString(Formatting.None);
            }
            else if (root["detail"] is { } d)
            {
                message = d.ToString();
            }
            else
            {
                // 額度、上游故障、沒有資料集那幾種走 {"error"/"note", "code"}。
                code = root.Value<string>("code");
                message = root.Value<string>("error") ?? root.Value<string>("note") ?? body;
            }
        }
        catch (JsonException)
        {
            // 不是 JSON（例如反向代理直接回的錯誤頁）：原樣留著給人看。
        }
        return new ApiException(status, message, code, correlationId, retryAfter);
    }
}
