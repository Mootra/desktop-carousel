# 桌面轮播

桌面轮播是一款 Windows 桌面快捷方式控件。它在主显示器底部显示一条透明弧线，快捷方式图标沿弧线缓慢循环。普通应用窗口可覆盖控件，透明空白区域保留桌面交互。

## 功能

- 自动读取个人桌面与公共桌面的 `.lnk` 和 `.url` 快捷方式，随文件的新增、删除和改名更新。
- 找到可用快捷方式后临时隐藏系统桌面图标。桌面文件保留在原位置，退出时恢复原有的图标显示状态。主进程异常退出时由恢复进程处理图标恢复。
- 鼠标悬停时暂停轮播，滚轮可手动拨动，单击图标会打开原快捷方式。Windows 关闭界面动画时，仍可使用滚轮浏览。
- 按 `Ctrl+Alt+D`（被占用时依次改用 `Win+Alt+D`、`Ctrl+Alt+Shift+D`）可在任意窗口之上唤出轮播，再按一次回到桌面层；`Esc` 或点击其他窗口也会收起。
- 托盘菜单可开关「开机自启动」，登录 Windows 后静默启动。启动项写在当前用户的 Run 注册表键，不需要管理员权限，关掉开关即删除。
- 指针停留后，快捷方式名称会显示在图标上方。键盘焦点也会显示名称。控件根据所在区域的 Windows 壁纸亮度调整文字和阴影，在背景明暗混杂时为名称加一层局部底色。

## 下载与使用

在 [GitHub Releases](https://github.com/Mootra/desktop-carousel/releases/latest) 下载 `DesktopCarousel-win-x64.zip`。解压后双击 `DesktopCarousel.exe`。便携包包含所需的 .NET 运行时。

右键控件上的图标或任务栏通知区域中的「桌面轮播」，可唤出/收起轮播、暂停轮播、恢复桌面图标、开关开机自启动、打开桌面文件夹或退出。「快捷方式」菜单也支持键盘操作。

当前便携包面向 Windows x64。已在 Windows 11 x64、150% 显示缩放下验证桌面层显示、轮播、快捷方式更新和图标恢复。

## 从源码构建

在 Windows 上安装 .NET 10 SDK，然后在项目根目录运行。

```powershell
dotnet run
```

生成 Windows x64 自包含可执行文件。

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o .local/publish
```
