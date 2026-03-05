using FluentAssertions;
using System.Text.RegularExpressions;

namespace FC.Engine.Infrastructure.Tests.Storage;

public class CssFallbackTests
{
    [Fact]
    public void CSS_Variables_Have_Fallback_Values_For_Tenant_Palette()
    {
        var appCss = File.ReadAllText(GetProjectPath("src", "FC.Engine.Admin", "wwwroot", "css", "app.css"));
        var portalCss = File.ReadAllText(GetProjectPath("src", "FC.Engine.Portal", "wwwroot", "css", "portal.css"));

        appCss.Should().Contain("var(--color-primary, #006B3F)");
        appCss.Should().Contain("var(--color-secondary, #C8A415)");

        portalCss.Should().Contain("var(--color-primary, #006B3F)");
        portalCss.Should().Contain("var(--color-secondary, #C8A415)");

        // Guard against tenant-color vars without explicit fallbacks.
        Regex.IsMatch(appCss + portalCss, @"var\(--color-[a-z-]+\)").Should().BeFalse();
    }

    private static string GetProjectPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "FCEngine.sln")))
            {
                return Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root from test base directory.");
    }
}
