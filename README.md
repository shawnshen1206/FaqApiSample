# API 使用說明文件

## API 概述
本 API 用於接收聊天記錄與設定，並返回機器人回應的結果。

### URL
Post https://www.sol-idea.com.tw/back/api/CompletionBot/SimplifiedFAQ

### 請求格式
| KEY            | VALUE                |
| -------------- | -------------------- |
| Content-Type   | multipart/form-data  |

### 請求資料範例
#### Layer 1
| KEY            | VALUE                |
| -------------- | -------------------- |
| jsonChatRoomVM | json 字串化後的字典，範例 |
|                | `{                   |
|                | "ApiKey": "your_key",|
|                | "LogChatLogHistorySN": -1,|
|                | "ChatLogs": [{"HumanContent": "你好阿"}]|
|                | }`                   |

#### Layer 2
| KEY                   | VALUE                       |
| --------------------- | --------------------------- |
| ApiKey                | 你的 api key                |
| LogChatLogHistorySN   | 想要接續對話紀錄的對話編號，如果沒有填 -1 |
| ChatLogs              | 將要送給機器人的字串放在 HumanContent     |

### 請求範例
curl --location 'https://www.sol-idea.com.tw/back/api/CompletionBot/SimplifiedFAQ' --form 'jsonChatRoomVM="{
\\\"ApiKey\\\": \\\"your_key\\\", 
\"LogChatLogHistorySN\": -1,
\"ChatLogs\": [{\"HumanContent\": \"你好阿\" }]
}"'

### 回應資料範例
#### Layer 1
| KEY        | VALUE                      |
| ---------- | -------------------------- |
| jsonData   | json 字串化後的資料，範例     |
|            | `{                         |
|            | "ApiKey": "your_key",      |
|            | "LogChatLogHistorySN": 1234,|
|            | "ChatLogs": [{"HumanContent": "你好阿", "AIContent": "你好！有什麼我可以幫助你的嗎？"}]|
|            | }`                         |

#### Layer 2
| KEY                  | VALUE                     |
| -------------------- | ------------------------- |
| ApiKey               | 你的 api key              |
| LogChatLogHistorySN  | 本次對話的對話編號         |
| ChatLogs             | 機器人的回應會放在 AIContent |
