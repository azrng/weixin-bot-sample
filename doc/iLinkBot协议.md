前言
微信终于"松口"了。

官方开放了 iLink Bot API，配套发布 @tencent-weixin/openclaw-weixin npm 包，让开发者第一次能用正规渠道给微信做自动化。

本文不聊"生态开放"的宏大叙事，只做一件事：把这套协议掰开揉碎，讲清楚它是什么、怎么用、踩坑点在哪。

全文偏硬核，建议收藏备查。

一、整体架构：三层蛋糕
先上一张全景图，心里有个底：

图片
简单说：上层管逻辑，中层管文件，底层管通道。

两个核心域名记住就行：

域名	干嘛的
https://ilinkai.weixin.qq.com	所有 API 请求走这里
https://novac2c.cdn.weixin.qq.com/c2c	图片/语音/视频等媒体文件走这里
二、鉴权机制：你是谁？
每一个 API 请求，都要告诉微信"我是合法的"。鉴权信息放在 HTTP Header 里。

2.1 请求头长这样
 Content-Type: application/json
 AuthorizationType: ilink_bot_token
 X-WECHAT-UIN: <base64 编码的随机数>
 Authorization: Bearer <bot_token>
2.2 X-WECHAT-UIN：一个有意思的设计
 functionrandomWechatUin() {
   // 生成随机 uint32
   constuint32=crypto.randomBytes(4).readUInt32BE(0);
   // 转十进制字符串 → Base64 编码
   returnBuffer.from(String(uint32), "utf-8").toString("base64");
 }
注意：每次请求都换一个。这是经典的防重放攻击设计——即使有人截获了你的请求，也没法原样重发。

2.3 bot_token 怎么来的？
通过扫码登录获取。别急，下一节详细讲。

三、API 端点速查
做开发的时候，这张表会反复用到，建议截图保存：

端点	方法	功能	关键参数
/ilink/bot/get_bot_qrcode	GET	获取登录二维码	?bot_type=3
/ilink/bot/get_qrcode_status	GET	轮询扫码状态	?qrcode=xxx
/ilink/bot/getupdates	POST	长轮询收消息	{ get_updates_buf }
/ilink/bot/sendmessage	POST	发送消息	{ msg }
/ilink/bot/getuploadurl	POST	获取 CDN 上传地址	{ filekey, md5, len }
/ilink/bot/getconfig	POST	获取配置	{ base_info }
/ilink/bot/sendtyping	POST	发送"正在输入"状态	{ to_user_id, typing_ticket }
一共就 7 个端点，不多，全记住也没压力。

四、核心流程详解
4.1 二维码登录：从扫码到拿 Token
这是一切的起点。流程不复杂，但有几个坑要注意。

图片
几个关键细节：

bot_type=3 是硬编码的，对应这个特定的账号类型，别瞎改

二维码过期了会自动刷新，最多 3 次

整个登录超时 480 秒（8 分钟），够你慢慢找手机了

4.2 长轮询收消息：核心中的核心
登录完成后，最重要的事就是收消息。iLink 用的是经典的长轮询（Long Polling）模式。

 letgetUpdatesBuf="";

asyncfunctionpollMessages() {
  constresp=awaitfetch(`${BASE}/ilink/bot/getupdates`, {
    method: "POST",
    headers: buildHeaders(),
    body: JSON.stringify({
      get_updates_buf: getUpdatesBuf,
      base_info: { channel_version: "1.0.3" }
    })
  });

  const { msgs, get_updates_buf: newBuf } =awaitresp.json();
  getUpdatesBuf=newBuf??getUpdatesBuf;  // ⚠️ 必须更新！
  returnmsgs?? [];
 }
⚠️ 踩坑警告：get_updates_buf 就是你的"位置书签"，类似数据库游标。每次请求完必须用返回的新值替换旧值，否则你会一直收到重复消息，陷入死循环。

其他要点：

超时时间 35 秒——没消息就等 35 秒返回空

连续失败 3 次后，自动退避 30 秒再重试，避免把服务器打爆

4.3 发消息：有个字段千万别漏
 asyncfunctionsendMessage(toUserId, text, contextToken) {
  awaitfetch(`${BASE}/ilink/bot/sendmessage`, {
    method: "POST",
    headers: buildHeaders(),
    body: JSON.stringify({
      msg: {
        to_user_id: toUserId,
        message_type: 2,      // 1=用户消息, 2=BOT消息
        message_state: 2,     // 2=FINISH（消息发送完毕）
        context_token: contextToken,  // ⚠️ 必须带上！
        item_list: [
          { type: 1, text_item: { text } }
        ]
      }
    })
  });
 }
敲黑板：context_token 是消息的"身份证"。每条收到的消息都带着一个，你回复的时候必须原样带上，否则微信不知道你在回复哪条消息，对话就乱了。

这个 token 建议做内存缓存 + 磁盘持久化，服务重启也不怕丢。

五、消息类型全景
5.1 五种消息类型
type	类型	数据结构	一句话说明
1	文本	TextItem { text }	最基础，够用
2	图片	ImageItem { media, thumb_media, aeskey, ... }	含缩略图，CDN 加密
3	语音	VoiceItem { media, encode_type, playtime, text }	自带 ASR 转文字
4	文件	FileItem { media, file_name, md5, len }	支持任意文件
5	视频	VideoItem { media, thumb_media, video_size, ... }	含封面图
5.2 语音编码格式
encode_type	格式	说明
1	PCM	原始音频数据
5	AMR	移动端老朋友
6	SILK	微信"祖传"格式，默认就是它
7	MP3	通用格式
💡 彩蛋：语音消息自带 text 字段——微信已经帮你做好了语音转文字（ASR），直接拿来用就行，不需要自己接 Whisper 之类的模型。

5.3 消息 ID 格式
真人用户：xxx@im.wechat

Bot：xxx@im.bot

一看后缀就知道是谁发的，挺方便。

六、CDN 媒体加密：微信的"保险箱"
发图片、语音这些文件，不是明文传输的，要过一层加密。

6.1 加密算法：AES-128-ECB + PKCS7
 const { createCipheriv, createDecipheriv } =require("crypto");

functionencryptAesEcb(plaintext, key) {
  constcipher=createCipheriv("aes-128-ecb", key, null);
  returnBuffer.concat([cipher.update(plaintext), cipher.final()]);
}

functiondecryptAesEcb(ciphertext, key) {
  constdecipher=createDecipheriv("aes-128-ecb", key, null);
  returnBuffer.concat([decipher.update(ciphertext), decipher.final()]);
}

// 密文大小 = 明文填充到 16 字节边界
functionaesEcbPaddedSize(plaintextSize) {
  returnMath.ceil((plaintextSize+1) /16) *16;
 }
有人可能会说"ECB 模式不安全"——确实，密码学教科书里会告诉你 ECB 有弱点。但在这个场景下，每个文件用的是一次性随机密钥，加上 HTTPS 保底，实际风险是可控的。

6.2 上传流程（6 步走）
 1. 📂 读取文件 → 计算 MD5
2. 🔑 生成随机 AES-128 key（16 字节）
3. 📡 POST /ilink/bot/getuploadurl → 拿到 upload_param
4. 🔒 用 AES-128-ECB 加密文件
5. ⬆️ POST https://novac2c.cdn.weixin.qq.com/c2c/upload
     ?encrypted_query_param={upload_param}&filekey={filekey}
     Body: 加密后的文件
     Response Header: x-encrypted-param → download_param
 6. 💬 发消息时带上 download_param + aes_key
6.3 下载流程（2 步走）
 1. GET https://novac2c.cdn.weixin.qq.com/c2c/download
      ?encrypted_query_param={encrypt_query_param}
 2. 用消息里带的 aes_key 解密返回内容
简洁明了。

七、多账号与状态持久化
真实场景里，你可能要管好几个微信号。iLink 的多账号方案很清晰。

7.1 账号数据结构
 typeWeixinAccountData= {
   token?: string;       // Bearer token
   baseUrl?: string;     // API 基础 URL
   userId?: string;      // 微信用户 ID
   savedAt?: string;     // 保存时间戳
 };
7.2 本地存储结构
 ~/.openclaw/
 ├── openclaw-weixin/
 │   ├── accounts.json                      # 账号索引
 │   └── accounts/
 │       ├── {accountId}.json               # 账号凭证
 │       ├── {accountId}.sync.json          # 同步游标（get_updates_buf）
 │       └── {accountId}.context-tokens.json # 上下文 token 缓存
7.3 自动去重
同一个微信号重新登录后，系统会自动清理旧的账号记录，避免同一个 userId 存在多份 context_token，导致消息匹配混乱。

八、错误码
目前已知的错误码很少（是的，文档暂时就给了这么多）：

errcode	含义	怎么办
0	成功	🎉 很好，继续
-14	会话过期	暂停发请求，等着恢复或重新登录
随着 API 的迭代，错误码应该会越来越丰富。

九、实战场景：这套协议能玩出什么花样？
技术讲完了，来点有意思的——你拿它能做什么？

场景 1：个人 AI 助手 🤖
图片
朋友发消息过来，AI 先接着聊。你可以做到：

对话上下文管理（context_token 自动关联，天然支持）

多模态理解（图片 + 文字，Vision 模型直接上）

工具调用（天气查询、搜索引擎、执行命令……）

💡 小技巧：用 sendtyping 发送"正在输入"状态，用户等 AI 回复的时候不会干等着，体验瞬间拉满。

场景 2：程序员的微信控制台 🖥️
图片
用微信当运维控制台，想想就爽：

CI/CD 构建完了？微信通知。

服务器 CPU 飙了？微信报警。

想重启服务？发条指令，搞定。

以前搞这些，需要自建 Webhook + 通知系统 + 一堆配置。现在，微信就是你的控制台。

场景 3：知识库问答（RAG）📚
图片
把你的笔记、公司文档、产品手册丢进向量数据库，微信里直接问、直接答。

场景 4：家庭群管家 👨‍👩‍👧‍👦
爸妈不会折腾 App，但一定会发微信。

"今天天气怎么样" → 自动回复

"提醒我 3 点吃药" → 定时推送

"这周菜价多少" → 查询返回

💡 关键洞察：语音消息自带 ASR 结果，老人只管说，机器人只管听，零学习成本。

场景 5：轻量客服系统 💬
多账号管理 + 常见问题自动回复 + 复杂问题转人工。每个对话用 context_token 隔离，天然支持上下文。

场景 6：自动化工作流 ⚡
收到微信指令 → 触发 GitHub Actions

监控 RSS / 网站更新 → 推送到微信

定时任务提醒（每天早 8 点天气预报）

十、手把手实测：从 0 到收发消息
光看协议文档不过瘾，我们直接写脚本实测。下面是我亲测的完整过程，分三步走：登录 → 验证连接 → 收发消息。

所有测试脚本已开源，项目结构：

 weixin-clawbot/
 ├── scripts/
 │   ├── login.js            # Step 1: 扫码登录
 │   ├── test-connection.js  # Step 2: 验证连接 + 自动回复
 │   └── send-message.js     # Step 3: 主动发消息
 ├── package.json
 └── .weixin-credentials.json  # 登录后自动生成
10.1 环境准备
 # 克隆项目
 git clone <项目地址>
 cd weixin-iLink
 
 # 安装依赖（只有一个 qrcode 包，用来在终端显示二维码）
 npm install
package.json 里已经配好了快捷命令：

 {
   "scripts": {
     "login": "node scripts/login.js",
     "test": "node scripts/test-connection.js",
     "send": "node scripts/send-message.js"
   }
 }
10.2 Step 1：扫码登录
 npm run login
脚本会做这些事：

请求二维码：GET /ilink/bot/get_bot_qrcode?bot_type=3

终端渲染 ASCII 二维码：用 qrcode 包把 URL 转成终端可显示的 ASCII 图案

轮询扫码状态：每秒请求一次 get_qrcode_status

登录成功：保存凭证到 .weixin-credentials.json

图片
打开微信扫一扫，进行扫描登入：

图片
因为之前已经连接到openclaw平台了，再次扫码会踢掉原有的连接

点击 继续连接，后台日志输出，登入成功!

图片
凭证文件长这样（自动生成，权限设为 600）：

 {
   "token": "eyJhbGciOiJIUzI1NiIs...",
   "baseUrl": "https://ilinkai.weixin.qq.com",
   "botId": "ilink_bot_xxxx",
   "userId": "xxxx@im.bot",
   "savedAt": "2026-03-25T01:00:00.000Z"
 }
💡 注意：如果已有凭证文件，脚本会提示你。想重新登录，删掉 .weixin-credentials.json 再跑就行。

10.3 Step 2：验证连接 + 自动回复
登录拿到 token 后，来验证它是否有效：

 npm run test
这个脚本做了两件事：

第一，验证 Token：调一次 getupdates，看返回码是不是 0。

图片

第二，进入自动回复模式：持续长轮询，收到用户消息后自动回一条 🤖 收到你的消息: "xxx"。

微信端发消息测试：

图片
后端收到消息，并回复信息：
图片

踩坑记录：如果 errcode 返回 -14，说明会话过期了，需要回到 Step 1 重新扫码登录。脚本里做了这个判断，会直接提示你。

10.4 Step 3：主动发消息
前两步是"被动收消息"，这一步是主动出击——你想给谁发消息就给谁发。

 # 直接给指定用户发消息
npm run send --"abc123@im.wechat""你好，这是一条主动消息"

# 或者，等对方先发消息，然后自动回复
npm run send ---w"收到你的消息啦！"

# 列出已知用户（之前聊过的）
 npm run send ---l
直接发送模式：

 📤 发送消息给: abc123@im.wechat
 📝 内容: 你好，这是一条主动消息
 
 ⌨️  正在输入...
 ✅ 发送成功！
后端命令行发送消息测试：

图片

微信端收到消息：

图片

这个脚本有几个技术亮点值得注意：

① context_token 的持久化管理
 constCONTEXT_TOKEN_PATH=".weixin-context-tokens.json";
 
 functionsaveContextToken(userId, token) {
   consttokens=loadContextTokens();
   tokens[userId] =token;
   fs.writeFileSync(CONTEXT_TOKEN_PATH, JSON.stringify(tokens, null, 2));
 }
每次收到消息，都会把 context_token 存到本地文件。这样即使重启脚本，也能继续和之前的用户对话——不需要等对方重新发消息。

② sendtyping —— "正在输入" 的体验细节
 asyncfunctionsendTyping(baseUrl, token, userId, typingTicket) {
  if (!typingTicket) return;
  awaitfetch(`${baseUrl}/ilink/bot/sendtyping`, {
    method: "POST",
    headers: buildHeaders(token),
    body: JSON.stringify({
      ilink_user_id: userId,
      typing_ticket: typingTicket,
      status: 1,
      base_info: { channel_version: "1.0.3" },
    }),
  });
 }
发消息之前，先调一下 sendtyping。对方的微信界面顶部会显示 "对方正在输入..."——这个小细节，能让机器人回复的体验从"冷冰冰的机器"变成"像个真人在打字"。

<!-- 📸 截图占位：微信显示"对方正在输入..."的截图 -->

③ 获取 typing_ticket
typing_ticket 不是凭空来的，需要先调 getconfig 获取：

 asyncfunctiongetConfig(baseUrl, token, userId) {
  constresponse=awaitfetch(`${baseUrl}/ilink/bot/getconfig`, {
    method: "POST",
    headers: buildHeaders(token),
    body: JSON.stringify({
      ilink_user_id: userId,
      base_info: { channel_version: "1.0.3" },
    }),
  });
  returnawaitresponse.json();
 }
完整的发消息流程就变成了：getconfig → sendtyping → sendmessage——三步走，体验拉满。

10.5 测试总结：实测数据
经过以上三步测试，我们验证了：

测试项	结果	备注
二维码获取	✅	ASCII 终端渲染正常
扫码登录	✅	Token 获取成功
Token 有效性验证	✅	ret=0 确认有效
长轮询收消息	✅	35 秒超时，消息实时到达
文本消息回复	✅	context_token 机制正常
主动发送消息	✅	需要有效的 context_token
"正在输入" 状态	✅	typing_ticket 机制正常
会话过期检测	✅	errcode=-14 正确识别


十一、限制与风险：丑话说在前头
限制	说明
🚫 无历史消息 API	只有游标往前走，不能"回头看"
❓ 速率限制未公开	能发多快？多少条？得自己试
📱 需要扫码登录	不能完全无人值守，需要人工介入
🤔 群聊支持不明确	有 group_id 字段，但权限和能力文档没说清
官方条款里的"大实话"：

腾讯只做"管道"，不存储消息内容（官方说法）

腾讯保留限速、封禁、终止服务的权利

翻译：今天能用 ≠ 明天一定能用

所以，重要业务务必留降级方案。别把鸡蛋全放在一个篮子里。

十二、相关资源
资源	地址
npm 包	@tencent-weixin/openclaw-weixin
CLI 工具	@tencent-weixin/openclaw-weixin-cli
npm 页面	https://www.npmjs.com/package/@tencent-weixin/openclaw-weixin
本文测试代码	weixin-iLink/scripts/ 目录
30 秒快速安装：

 npx -y @tencent-weixin/openclaw-weixin-cli install
 openclaw channels login --channel openclaw-weixin
结语
这篇文章覆盖了 iLink Bot API 的协议架构、鉴权机制、消息收发、CDN 加密、多账号管理，再到六大实战场景，最后用三个脚本从零跑通了完整的登录→收消息→发消息流程。

如果你打算动手试试，三条建议：

先跑通测试脚本 —— 确认登录和消息收发没问题

评估你的风险承受力 —— 核心业务要有 Plan B

关注官方动态 —— 协议还在早期，随时可能更新

微信开放这套 API，是一个信号。门已经开了，能走多深，取决于你的想象力。

有问题欢迎留言交流 👇

文中脚本的github地址：https://github.com/gallop/weixin-iLink.git