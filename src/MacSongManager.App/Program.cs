// Copyright (C) 2026 Antony Vo
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia;

namespace MacSongManager;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
