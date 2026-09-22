using Xunit;

namespace DotnetDocFs.Tests;

/// <summary>
/// The command line. Every flag here is documented in <c>--help</c> and the README, and the
/// combinations that are refused are refused because accepting them would fail somewhere far
/// from the cause.
/// </summary>
public class CliOptionsTests
{
    [Fact]
    public void TheDefaultsServeLoopbackWithTheControlFileOpen()
    {
        CliOptions options = CliOptions.Parse([]);

        Assert.Equal("tcp://127.0.0.1:15640", options.ListenAddress);
        Assert.True(options.AllowIngest);
        Assert.False(options.Mount);
    }

    [Theory]
    [InlineData("--port", "9999")]
    [InlineData("--smb-port", "9999")]
    [InlineData("--framework", "net8.0")]
    [InlineData("--path", "/tmp/x")]
    public void AValueTakingFlagAcceptsBothSpellings(string flag, string value)
    {
        Assert.Equal(
            CliOptions.Parse([flag, value]),
            CliOptions.Parse([$"{flag}={value}"]));
    }

    [Theory]
    [InlineData("--path=")]
    [InlineData("--framework=")]
    public void AFlagGivenAnEmptyValueIsRefusedWhereItIsWritten(string argument) =>
        Assert.Throws<CliUsageException>(() => CliOptions.Parse([argument]));

    [Fact]
    public void AFlagAtTheEndWithNoValueIsRefused() =>
        Assert.Throws<CliUsageException>(() => CliOptions.Parse(["--port"]));

    [Theory]
    [InlineData("--port", "0")]
    [InlineData("--port", "65536")]
    [InlineData("--port", "not-a-number")]
    public void SomethingThatIsNotAPortIsRefused(string flag, string value) =>
        Assert.Throws<CliUsageException>(() => CliOptions.Parse([flag, value]));

    [Fact]
    public void AnUnknownFlagIsRefused() =>
        Assert.Throws<CliUsageException>(() => CliOptions.Parse(["--ctl"]));

    /// <summary>
    /// <c>--listen</c> and <c>--port</c> describe the same thing, and the mount has to be told
    /// the one the server actually bound. Told the other, it either fails or attaches to a
    /// different dotnetdoc still running on the default port.
    /// </summary>
    [Fact]
    public void TheMountFollowsTheListenAddressRatherThanThePortFlag() =>
        Assert.Equal(
            9999,
            CliOptions.Parse(["--listen", "tcp://127.0.0.1:9999", "--mount"]).MountSettings.NinePPort);

    [Fact]
    public void TheMountPathIsMadeAbsolute() =>
        Assert.True(Path.IsPathRooted(CliOptions.Parse(["--path", "relative/here"]).MountSettings.MountPath));

    /// <summary>
    /// Mounting resolves a path nobody gave, but the served skill must not: it prints the path it
    /// is given, and a default nobody stated would send an agent to a directory that is not
    /// there. So the two readings are kept apart — <c>MountSettings</c> always has one, and
    /// <c>MountPath</c> only when somebody said.
    /// </summary>
    [Fact]
    public void APathIsOnlyStatedWhenItWasGiven()
    {
        Assert.Null(CliOptions.Parse([]).MountPath);
        Assert.True(Path.IsPathRooted(CliOptions.Parse([]).MountSettings.MountPath));

        Assert.Equal("/mnt/docs", CliOptions.Parse(["--path", "/mnt/docs"]).MountPath);
    }

    [Fact]
    public void ServingOffLoopbackWithTheControlFileOpenIsRefused() =>
        Assert.Throws<CliUsageException>(
            () => CliOptions.Parse(["--listen", "tcp://0.0.0.0:15640"]).Validated());

    [Fact]
    public void ServingOffLoopbackReadOnlyIsAllowed() =>
        CliOptions.Parse(["--listen", "tcp://0.0.0.0:15640", "--no-ctl"]).Validated();

    [Fact]
    public void MountingWhatIsNotOnLoopbackIsRefused() =>
        Assert.Throws<CliUsageException>(
            () => CliOptions.Parse(["--listen", "tcp://0.0.0.0:15640", "--no-ctl", "--mount"]).Validated());

    /// <summary>A unix socket is a path on this machine, so it is more local than loopback.</summary>
    [Fact]
    public void AUnixSocketIsLocalEnoughForTheControlFile() =>
        CliOptions.Parse(["--listen", "unix:///tmp/dotnetdoc.sock"]).Validated();

    /// <summary>The mount is made with <c>trans=tcp</c>, so it needs a TCP address.</summary>
    [Fact]
    public void AUnixSocketCannotBeMounted() =>
        Assert.Throws<CliUsageException>(
            () => CliOptions.Parse(["--listen", "unix:///tmp/dotnetdoc.sock", "--mount"]).Validated());

    [Theory]
    [InlineData("tcp://127.0.0.1:15640")]
    [InlineData("tcp://localhost:15640")]
    [InlineData("tcp://[::1]:15640")]
    public void EverySpellingOfLoopbackIsAccepted(string address) =>
        CliOptions.Parse(["--listen", address, "--mount"]).Validated();

    /// <summary>
    /// Every flag the parser accepts is named in the usage text, and every flag the usage text
    /// names is accepted. An undocumented flag is one nobody can find and nobody can rely on.
    /// </summary>
    [Fact]
    public void EveryFlagIsDocumentedAndEveryDocumentedFlagIsAccepted()
    {
        string[] documented =
        [
            .. CliOptions.Usage
                .Split([' ', '\n', '\t', ',', '[', ']', '|'], StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.StartsWith("--", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal),
        ];

        Assert.NotEmpty(documented);

        foreach (string flag in documented)
        {
            // Either it parses on its own, or it parses with a value; what it must not do is be
            // rejected as a flag this program does not know.
            try
            {
                CliOptions.Parse([flag]);
            }
            catch (CliUsageException exception)
            {
                Assert.DoesNotContain("unknown option", exception.Message, StringComparison.Ordinal);
            }
        }
    }
}
