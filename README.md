# GufoFAQ 前台問答 API 使用說明

以 `X-Widget-Token` 認證的**前台公開問答介面**，給客戶自己的網站或後端程式呼叫。

| 端點 | 做什麼 |
|---|---|
| `POST /api/public/faq` | 問一題，**一次回完整 JSON**（不用處理串流） |
| `POST /api/public/ask` | 問一題，**逐字串流**回答（SSE） |
| `POST /api/public/rating/{log_ref}` | 對某一則回答按讚／倒讚 ＋ 意見回饋 |
| `POST /api/public/share` | 把某一則問答變成一條唯讀分享連結 |
| `GET /api/public/welcome` | 取這個租戶設定的開場白 |

---

## 一、開始之前

### 基底網址

```
https://<你的 GufoFAQ 網域>
```

由我們提供。本文件之後一律以 `$BASE` 代表它。

### 取得 widget 權杖

租戶管理者在產品介面的〈Widget 權杖〉頁建立，取得形如 `wgt_` 開頭的字串。

- 建立時**必須填「允許網域」**，而且每一個請求都會被拿來比對（見下一節）。
- **可以有多把**（例如正式站與測試站各一把），可個別撤銷；撤銷後立刻失效。
- 它**依設計是公開的**（客戶把它嵌在自己網頁的 JS 裡），所以它不是祕密——真正的邊界是
  「允許網域」＋速率上限。不要拿它當後台憑證用，它只碰得到本文件這幾支端點。

### 共同標頭

| 標頭 | 值 | 必要性 |
|---|---|---|
| `X-Widget-Token` | 你的權杖 | 全部端點（`/welcome` 也要） |
| `Origin` | 權杖「允許網域」清單裡的其中一個 | **見下方說明** |
| `Content-Type` | `application/json` | 送 body 的端點 |

**⚠️ 從後端程式呼叫時要自己帶 `Origin`。** 瀏覽器會自動帶，後端不會——而 `Origin` 與
`Referer` 兩個都沒有時，請求會被判成「來源不允許」而 `403`。規則：

1. 有 `Origin` → 拿它比對允許網域。
2. 沒有 `Origin`、有 `Referer` → 從 `Referer` 解出 `scheme://host[:port]` 再比對。
3. 兩個都沒有 → `403`（`origin not allowed`）。

比對是**正規化後的精確比對**（轉小寫、去掉結尾斜線），所以 `https://ACME.example/` 與
`https://acme.example` 視為相同；但 `https://acme.example` 與 `https://www.acme.example`
是不同的兩個來源，要各自登記。

### 共同閘門

| 順序 | 檢查 | 不通時 |
|---|---|---|
| 1 | `X-Widget-Token` 有效且未撤銷 | `401`，`detail` 為 `invalid or missing widget token` |
| 2 | 來源網域在允許清單內 | `403`，`detail` 為 `origin not allowed` |
| 3 | 租戶使用期 | `403`，`detail.code` 為 `subscription_expired`／`account_frozen`／`disclaimer_required` |
| 4 | 租戶功能開通（問答要 `ask`、分享要 `history`） | `403`，`detail` 指名缺哪一項 |
| 5 | 該租戶的請求速率（預設每分鐘 600 次） | `429` ＋ `Retry-After` |
| 6 | 該租戶的問答額度 | `429`，`code` 為 `quota_exceeded` |

**「已撤銷」與「從來不存在」回同一種 `401`**（不告訴呼叫端這把權杖曾經存在）。

### 錯誤信封

`detail` 有兩種形狀，另有一種帶 `error`／`code` 的：

```json
{ "detail": "invalid or missing widget token" }
{ "detail": { "code": "subscription_expired" } }
{ "error": "問答次數不足", "code": "quota_exceeded" }
```

**用 HTTP 狀態碼 ＋ `code` 分流，不要比對句子**（句子會隨租戶語言變）。回應標頭一律帶
`X-Correlation-ID`，回報問題時附上它。

---

## 二、非串流問答

```
POST $BASE/api/public/faq
```

```json
{ "query": "你們的營業時間？", "room_ref": null }
```

| 欄位 | 必填 | 說明 |
|---|---|---|
| `query` | 是 | 問題。不可為空白；長度上限由伺服器決定（超過即 `422`，訊息會說明） |
| `room_ref` | 否 | 上一輪回應給的 `room_ref`，帶了就是**接續同一段對話**；不帶或 `null` ＝開新對話 |

**只收這兩個欄位**——多送任何未知欄位一律 `422`。這是刻意的：`room_ref` 拼錯成 `roomRef`
或 `room_id` 而被靜默忽略的話，症狀是「追問接不上前文」，而答案照樣產生、沒有任何錯誤。

**回應 `200`**

```json
{
  "answer": "我們的營業時間是週一至週五 09:00–18:00。[[1]]",
  "sources": [ { "source_no": 1, "doc_name": "客服常見問題", "score": 0.82, "document": { "title": "營業時間", "content": "…" } } ],
  "log_ref": "1284.9a1f…",
  "room_ref": "77.3c8e…"
}
```

| 欄位 | 說明 |
|---|---|
| `answer` | 完整答案（Markdown）。裡面可能有 `[[N]]` 引用標記，見〈六〉 |
| `sources` | 這一則答案的來源，見〈五〉 |
| `log_ref` | 這一則問答的**簽章參照**。評分與分享都用它 |
| `room_ref` | 下一輪要帶回來的對話參照 |

**回應裡沒有裸的數字編號**。`log_ref`／`room_ref` 是綁租戶的簽章字串，只在你的租戶下有效
——不要嘗試自己組、也不要對它做算術。

---

## 三、串流問答（SSE）

```
POST $BASE/api/public/ask
```

Body 與 `/faq` 完全相同。回應是 `Content-Type: text/event-stream`，**一次 POST 就開始串流**
（不需要先取號再另外開一條 GET）。

每一筆記錄的形狀固定是：

```
data: {"chunk_type":"message","content":"我們的營"}\n\n
```

- 記錄邊界是**連續兩個換行**；每一筆的 `data: ` 後面是一個完整 JSON。
- **答案 ＝ 依序串接所有 `chunk_type == "message"` 的 `content`。**
- 串流結束就是 HTTP 回應結束（**沒有** `[END]` 之類的哨兵字串）。

| `chunk_type` | 要做的事 |
|---|---|
| `message` | 串接 `content`，這是答案本體 |
| `chat_room` | 中繼資料，見下表。**通常是最後一筆有內容的記錄** |
| `error` | 把 `content` 顯示給使用者；這一輪不會再有答案 |
| `thinking` | 模型的思考過程；要就顯示，不要就忽略 |
| `status` | 進度（跑到哪一步），可忽略 |
| `end` | 結束標記，可忽略 |
| `agent_tool_call`／`agent_tool_result` | agent 模式下的工具軌跡，可忽略 |

**不認得的 `chunk_type` 一律忽略、不要當成錯誤**——之後可能會多出新的型別。

`chat_room` 記錄的 `data`：

| 欄位 | 說明 |
|---|---|
| `log_ref` | 這一則問答的簽章參照（評分／分享用） |
| `room_ref` | 下一輪要帶回來的對話參照 |
| `latest_chat_log_id` | 這一則的顯示用編號（僅供你自己對照，**不能拿去評分或分享**） |
| `search_results` | 來源清單，見〈五〉 |
| `suggest_questions` | 建議追問（字串陣列），可畫成可點選的按鈕 |
| `answer_source` | `"qa_direct"` ＝這一則是 QA 集裡的**逐字**答案；`null` ＝模型生成 |
| `usage` | 這一輪的用量 |
| `ttft_ms`／`latency_ms` | 首字延遲與總延遲（毫秒） |

---

## 四、多輪續談

1. 第一輪不帶 `room_ref`。
2. 從回應拿 `room_ref`（`/faq` 在 body、`/ask` 在 `chat_room` 記錄裡）。
3. 下一輪把它放進 body 的 `room_ref`。
4. **每一輪都會回一個新的 `room_ref`，下一輪用最新的那個。**

`room_ref` 綁租戶簽章，所以不同訪客之間無法互相接續對話。要開新對話就不要帶它。

---

## 五、來源清單

`/faq` 的 `sources` 與 `/ask` 的 `chat_room.data.search_results` 是同一份東西，但**刻意不對稱**：

| | `/api/public/faq` | `/api/public/ask` |
|---|---|---|
| `document`（命中文件的欄位值） | **有**，只含內容欄位 | **沒有**（SSE 上不送） |

要在自己的畫面上顯示來源卡的內容，用 `/faq`；`/ask` 適合「只顯示答案與來源筆數」的串流介面。

逐筆常見的欄位：

| 欄位 | 說明 |
|---|---|
| `source_no` | 引用編號。**每一筆都有**，而且集合恰好是答案裡合法可寫的 `[[N]]` 全集 |
| `doc_name` | 這一筆的可辨識名稱 |
| `document_id` | 引擎那一側的文件編號 |
| `score` | 相似度 |
| `data_source` | 來源檔名（**只有檔名**，不含路徑） |
| `chunk_index`／`last_modified`／`search_mode` | 命中的分塊序、最後修改、檢索模式 |
| `document` | 只有 `/faq` 有。命中文件的內容欄位（`title`／`content`／`source`／`date`／`category`／`number`／`flag`／`tags`／`note1`~`note10`／`date2`／`date3`／`search`），逐份只含實際有值的那些 |

**保證**（不論引擎那一側之後回什麼）：

- **不會有 `index`**（租戶的內部索引名）。
- **`document` 不會有 `privilege`**（「誰可以看這份文件」的名單）。
- `data_source` 只有檔名。
- 沒有 `source_no` 的候選不會出現在清單裡——出得去的每一筆都是模型真的看過的。

其餘欄位由引擎決定，可能會多。**請以「有就用、沒有就跳過」的方式讀它**，不要假設固定欄位集。

---

## 六、答案裡的 `[[N]]` 引用

答案內文可能出現 `[[1]]`、`[[3]]` 這種標記，`N` 對應 `sources[].source_no`。

要把它畫成可點的徽章時，**用 `source_no` 去查對應那一筆**，不要用陣列位置、也不要自己算
「模型看了幾筆」——`sources` 裡的每一筆都已經是模型看過的，號碼就在資料裡。

查不到對應 `source_no` 的標記（理論上不會發生）就原樣顯示文字，不要吃掉它。

---

## 七、評分

```
POST $BASE/api/public/rating/{log_ref}
```

```json
{ "rating": "up", "feedback": "回答很有幫助" }
```

| 欄位 | 必填 | 說明 |
|---|---|---|
| `rating` | 是 | 只能是 `"up"` 或 `"down"`（其他值 `422`） |
| `feedback` | 否 | 意見回饋。長度上限由伺服器決定（超過即 `422`） |

**只收這兩個欄位**，多送未知欄位一律 `422`——`feedback` 打錯欄名而被靜默丟掉的話，
訪客看到「感謝回饋」而他打的字一個都沒留下，而且沒有第二次機會要回來。

`log_ref` 放在路徑上，取自問答回應。**格式錯、簽章不符、不是你租戶的、那一則不存在**——
四種一律 `404 chat log not found`（不分辨原因）。同一則可以重複評分，後送的覆蓋前一次。

---

## 八、分享

```
POST $BASE/api/public/share
```

```json
{ "log_ref": "1284.9a1f…" }
```

**回應 `200`**：`{ "token": "shr_…" }`

把 `token` 拼成 `$BASE/shared/{token}` 就是一條**唯讀**的分享連結，不需要任何憑證即可開啟。

- **同一則問答重複分享回同一條連結**（不會每次都產生新的）。
- 租戶在後台撤銷那條連結之後，再分享同一則會拿到**新的** token（舊的不會復活）。
- 這一支要租戶開通 `history` 功能；沒開通是 `403` 且會指名——**不是**回一條打不開的連結。

---

## 九、開場白

```
GET $BASE/api/public/welcome
```

**回應 `200`**：`{ "content": "您好，我是客服小幫手…" }`

租戶在產品介面設定；沒設過會回內建預設句（**不會**是空字串）。上游讀不到設定時回 `502`
——那時請沿用你自己畫面上的預設文案，不要把錯誤當成「這個租戶沒有開場白」。

---

## 十、錯誤一覽

| HTTP | `code`／`detail` | 意思 | 該怎麼做 |
|---|---|---|---|
| `401` | `invalid or missing widget token` | 權杖缺漏、錯誤或已撤銷 | 換一把有效的 |
| `403` | `origin not allowed` | `Origin`／`Referer` 不在允許網域內，或兩者都沒帶 | 後端呼叫要自己帶 `Origin` |
| `403` | `disclaimer_required` | 租戶還沒接受免責聲明 | 請租戶管理者完成 |
| `403` | `subscription_expired`／`account_frozen` | 使用期到期／帳號凍結 | 聯絡我們 |
| `403` | （句子指名功能） | `ask` 或 `history` 未開通 | 聯絡我們 |
| `409` | `no indexes` | 這個租戶還沒有任何資料集 | 先匯資料（見資料 API） |
| `409` | `upstream_rejected` | 這個請求引擎不受理（例如對話參照已失效） | **不要重試**；改成開新對話 |
| `422` | （欄位錯誤清單） | body 形狀不合（缺 `query`、空字串、超長、多送未知欄位） | 照訊息修正 |
| `429` | `quota_exceeded` | 問答額度用完 | 聯絡我們加額度 |
| `429` | `rate_limited` | 每分鐘請求數超限 | 照 `Retry-After` 等待 |
| `429` | `upstream_rate_limited` | 引擎端的當日用量已滿 | 照 `Retry-After` 退避重試 |
| `404` | `chat log not found` | `log_ref` 無效／不是你的／那一則不存在 | 重新取得 `log_ref` |
| `502` | `chat_error` | 跨服務故障 | 附關聯編號回報；不要無限重試 |
| `503` | `upstream_unavailable` | 引擎暫時不可用 | 照 `Retry-After` 重試 |

**三種「答不出來」要分開處理**：`429`（等一下再試）、`409`（重試永遠不會好，要改請求）、
`502`（我們這邊出問題了）。

串流那一支若在**開始串流之後**才失敗，狀態碼已經送出去了，錯誤會以 `chunk_type == "error"`
的記錄出現在串流裡——所以 SSE 的呼叫端**兩種都要處理**：HTTP 層的失敗，與串流中的 `error` 記錄。

---

## 十一、界線

以下是**預設值**，部署可調整；超過時的回應會說明實際生效的值。

| 項目 | 預設 |
|---|---|
| 每租戶每分鐘請求數（本文件這幾支共用一顆） | 600 |
| `query` 長度 | 由伺服器決定（超過即 `422`，訊息會說明） |
| `feedback` 長度 | 由伺服器決定（超過即 `422`） |
| 問答額度 | 逐租戶設定 |

**問答額度是「問了就扣」**：請求進到問答流程之後才失敗（引擎中斷、串流斷線）**照樣計次**，
不退還。在那之前被擋下的（憑證、來源網域、額度本身、欄位格式）不計次。

---

## 十二、從舊版 API 遷移

舊版（`/api/CompletionBot/*`）與現行介面的對應：

| 舊版 | 現行 |
|---|---|
| `multipart/form-data` ＋ 表單欄位夾一段 JSON 字串 | **直接送 JSON body** |
| body 裡的 `ApiKey` 欄位 | `X-Widget-Token` 標頭（＋後端呼叫要帶 `Origin`） |
| `JsonFormat` 信封（`{Error, Message, JsonData}`，`JsonData` 還要再解一次） | **回應就是資料本身** |
| 串流版要兩步（`POST` 取 `FocusLogChatLogSN` → 另開 `GET .../{sn}`） | **一步**：`POST /api/public/ask` 直接回 SSE |
| `data: "[END]"` 哨兵 | 串流結束就是回應結束；`message` 記錄之外還有 `chat_room` 等中繼型別 |
| 每一筆 SSE 是一段被 JSON 字串化的純文字 | 每一筆是一個 JSON 物件（`chunk_type` ＋ `content`／`data`） |
| `LogChatLogHistorySN`（整數，帶回去接續對話） | `room_ref`（綁租戶的簽章字串） |
| `FocusLogChatLogSN`（整數，用來評分） | `log_ref`（綁租戶的簽章字串） |
| `RatingType: 1/2/3` | `rating: "up"／"down"`（沒有「未評分」這個值；不評分就不要呼叫） |
| `ResponseFormat: 0/1`（Markdown／HTML） | 一律 Markdown（HTML 由呼叫端自己渲染） |
| `RequireSearchResults: 0/1` | 來源一律回；不需要就不讀 |
| `jsonFolders`（逐請求指定資料集與優先度） | 由租戶在產品介面的檢索設定決定，請求不再帶 |
| 數字錯誤碼（`3001`／`4001`／`4002`…） | HTTP 狀態碼 ＋ `code` 字串（見〈十〉） |
| 回應含 `IndexName`（資料集名） | 不再外送（內部識別）；來源用 `doc_name`／`document` 顯示 |

新增的能力：`[[N]]` 引用與 `source_no` 的對應、建議追問、`answer_source`（是不是 QA 逐字直答）、
延遲量測、分享連結、開場白、綁網域的可撤銷權杖。

---

## 範例程式

`Program.cs` 是一支可直接跑的 .NET 主控台程式。

```bash
dotnet run -- <指令>
```

| 指令 | 做什麼 |
|---|---|
| `faq` | 非串流問一題，印出答案與來源 |
| `ask` | 串流問一題，逐字印出來 |
| `chat` | 兩輪對話（第二輪帶 `room_ref` 接續前文） |
| `rating` | 問一題然後對它按讚 |
| `share` | 問一題然後產生分享連結 |
| `welcome` | 取開場白 |
| `all` | 依序跑完上面全部 |

跑之前先改檔案最上面三個常數：

```csharp
const string BaseUrlDefault = "https://<你的 GufoFAQ 網域>";
const string WidgetTokenDefault = "wgt_你的權杖";
const string OriginDefault = "https://你登記的網域";
```

或用環境變數 `GUFOFAQ_BASE_URL`／`GUFOFAQ_WIDGET_TOKEN`／`GUFOFAQ_ORIGIN`（優先於常數）。
