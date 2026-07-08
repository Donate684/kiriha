using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Services;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Xunit;

namespace Kiriha.Tests;

public class NotificationManualTests
{
    // Простая заглушка для супервизора фоновых задач, чтобы не тянуть зависимости
    private class DummyTaskSupervisor : IBackgroundTaskSupervisor
    {
        public Task Run(string name, Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            _ = operation(cancellationToken);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void ShowTestNotification()
    {
        // Тест будет работать только на Windows
        if (!OperatingSystem.IsWindows()) return;

        var tempSettings = Path.GetTempFileName();
        try
        {
            using var settings = new SettingsService(tempSettings);
            // Убеждаемся, что уведомления включены (по умолчанию так и есть)
            settings.Current.System.NotifyAppUpdate = true;

            var svc = new NotificationService(settings, new DummyTaskSupervisor());
            
            // Запускаем уведомление (это вызовет Windows Toast Notification)
            svc.NotifyAppUpdate("Привет! Это тестовое уведомление из Kiriha на Windows 11 🎉");
            
            // Ждем 2-3 секунды, чтобы COM-вызов успел отработать и показать уведомление
            Thread.Sleep(3000);
        }
        finally
        {
            if (File.Exists(tempSettings))
                File.Delete(tempSettings);
        }
    }
}
