// 测试用：伪装成 QQMusic.exe 的空进程，用于验证「跟随 QQ 音乐显示/隐藏」。
using System;
using System.Threading;

internal static class FakeQQ
{
    private static void Main()
    {
        Thread.Sleep(600000);
    }
}
