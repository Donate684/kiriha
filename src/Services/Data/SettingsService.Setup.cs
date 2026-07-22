using System.Linq;

namespace Kiriha.Services.Data;

public partial class SettingsService
{
    public bool NeedsFirstStartup()
    {
        var required = new[] { "language", "theme", "mal_login" };
        return Read(settings => required.Any(step => !settings.System.CompletedSetupSteps.Contains(step)));
    }

    public void CompleteSetupStep(string key)
    {
        var changed = false;
        Update(settings =>
        {
            if (!settings.System.CompletedSetupSteps.Contains(key))
            {
                settings.System.CompletedSetupSteps.Add(key);
                changed = true;
            }
        }, SettingsSection.System, save: false);

        if (changed) Save();
    }
}
