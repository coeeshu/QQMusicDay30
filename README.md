# QQMusicDay30 · 每日30首捕捉器

QQ音乐的「每日30首」、网易云音乐的「每日推荐」每天都会换，错过就找不回来了。这个小工具每天自动把它记下来，想听哪天的，一键放进你账号里的一个「工作区歌单」，直接在客户端里播放。

- 同时支持 **QQ音乐** 和 **网易云音乐**，各自登录、各自记录，侧边栏一键切换
- 每天自动记录当天的推荐（歌名、歌手、专辑、时长、封面）
- 自动补回服务器还保留的历史：QQ音乐手机端的「历史推荐」（约 7 天）、网易云的「历史日推」（黑胶 VIP，60 天内最近 5 次）
- 按日期浏览历史，或在所有历史里搜索歌名 / 歌手
- 「放到工作区歌单」：清空并写入那天的歌（顺序保持一致）。QQ音乐客户端几秒内自动刷新；网易云会直接开始播放这个歌单
- 「另存为独立歌单」：生成一个永久歌单，比如 `2026.10.18每日30`、`2026.10.22每日推荐`
- 深色 / 浅色 / 跟随系统三种主题，切换时从按钮处圆形扩散过渡
- 托盘常驻，可开机自启；打开QQ音乐 / 网易云时可自动弹出
- 独立进程，不注入、不修改音乐客户端

## 使用

1. 需要 Windows 10/11、[.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0) 和 WebView2 运行时（Win11 自带）
2. 从 Releases 下载 `QQDay30.exe` 运行
3. 在侧边栏选 QQ音乐 或 网易云音乐，点「登录」扫码（两个都想记录就各登录一次）
4. 之后它会在后台每 30 分钟检查一次，并在音乐客户端启动时检查，当天的推荐会被记录下来
5. 登录有效期：
   - QQ音乐的凭证只有 3 天，程序会在用满 1 天后自动续期（`music.login.LoginServer / Login`），正常情况下不需要反复扫码
   - 网易云的登录（`MUSIC_U`）约半年有效，到期后会弹出扫码窗口
   - 网易云每日推荐在早上 6 点更新，6 点前记录的是前一天的

> 首次运行会从「历史推荐」补回最近约 7 天（服务器只保留你近 30 天内最近 7 次打开过的每日30首）。更早的找不回来，之后就靠每天自动记录，所以建议保持开机自启；偶尔几天没开电脑，下次启动也会自动补回。

## 数据存放

都在 `%LOCALAPPDATA%\QQDay30\`：

| 文件 | 内容 |
|---|---|
| `day30.db` / `ncm_day30.db` | QQ音乐 / 网易云的历史记录（SQLite） |
| `cookie.json` / `ncm_cookie.json` | QQ音乐 / 网易云的登录凭证，**明文保存，不要发给别人** |
| `settings.json` | 设置（工作区歌单名、QQ音乐路径等） |
| `log.txt` | 运行日志 |

## 原理

### QQ音乐

使用 y.qq.com 网页版的接口（`u.y.qq.com/cgi-bin/musicu.fcg`），凭证来自内嵌 WebView2 中的网页登录：

| 功能 | 接口 |
|---|---|
| 读取每日30首 | `music.srfDissInfo.aiDissInfo / uniform_get_Dissinfo`，`dirid = 202` |
| 历史推荐（过去约 7 天） | `music.srfDissInfo.aiHistory / get_aiHistoryinfo`，`{dirid: 202, tid: 0}` 返回日期列表；加 `spec: {bSet: true, year, month, day}` 返回某一天 |
| 列出自建歌单 | `music.musicasset.PlaylistBaseRead / GetPlaylistByUin` |
| 新建歌单 | `music.musicasset.PlaylistBaseWrite / AddPlaylist` |
| 添加 / 删除歌曲 | `music.musicasset.PlaylistDetailWrite / AddSonglist`、`DelSonglist` |

日期取自每日30首封面上的日历图（`calendar/MMDD_300.jpg`）。批量添加的歌曲在歌单里是倒序显示的，所以写入时要倒着提交。

### 网易云音乐

使用 music.163.com 网页版的 weapi 接口（`/weapi/...`，参数经 AES + RSA 加密），凭证同样来自内嵌 WebView2 中的网页登录：

| 功能 | 接口 |
|---|---|
| 每日推荐 | `/weapi/v3/discovery/recommend/songs` |
| 历史日推的日期 / 某一天 | `/weapi/discovery/recommend/songs/history/recent`、`/history/detail {date}` |
| 列出歌单 / 新建歌单 | `/weapi/user/playlist`、`/weapi/playlist/create` |
| 读取 / 添加 / 删除歌曲 | `/weapi/v6/playlist/detail`、`/weapi/playlist/manipulate/tracks {op: add / del}` |
| 打开并播放歌单 | `orpheus://` + base64 的 `{"type":"playlist","id":…,"cmd":"play"}` |

每首新加的歌都会排到歌单最前面，所以同样倒着提交。

## 构建

```
cd src/QQDay30
dotnet publish -c Release
```

输出为 `dist/QQDay30.exe`（依赖框架的单文件）。

图标由 `python tools/make_icon.py` 生成（需要 Pillow），输出到 `src/QQDay30/Assets/`。

## 声明

本项目与腾讯 / QQ音乐、网易 / 网易云音乐无关，使用的是非官方接口，可能随时失效。仅供个人学习和管理自己账号的歌单使用，请勿用于任何商业或批量用途。

## License

[MIT](LICENSE)

## 🙏 鸣谢

感谢 [LINUX DO](https://linux.do) 社区。
