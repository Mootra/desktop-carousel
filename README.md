# 桌面轮播

桌面轮播是一款 Windows 桌面快捷方式控件。它在主显示器底部显示一条透明弧线，快捷方式图标沿弧线缓慢循环。普通应用窗口可覆盖控件，透明空白区域保留桌面交互。

## 功能

- 自动读取个人桌面与公共桌面的 `.lnk` 和 `.url` 快捷方式，随文件的新增、删除和改名更新。
- 找到可用快捷方式后临时隐藏系统桌面图标。桌面文件保留在原位置，退出时恢复原有的图标显示状态。主进程异常退出时由恢复进程处理图标恢复。
- 鼠标悬停时暂停轮播，滚轮可手动拨动，单击图标会打开原快捷方式。Windows 关闭界面动画时，仍可使用滚轮浏览。

## 下载与使用

在 [GitHub Releases](https://github.com/Mootra/desktop-carousel/releases/latest) 下载 `DesktopCarousel-win-x64.zip`。解压后双击 `DesktopCarousel.exe`。便携包包含所需的 .NET 运行时。

右键控件上的图标或任务栏通知区域中的「桌面轮播」，可暂停轮播、恢复桌面图标、打开桌面文件夹或退出。「快捷方式」菜单也支持键盘操作。

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
