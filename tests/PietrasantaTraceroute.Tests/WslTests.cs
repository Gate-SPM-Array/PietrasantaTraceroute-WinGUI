using System.Text;
using PstrGui;
using Xunit;

namespace PietrasantaTraceroute.Tests
{
    public class WslTests
    {
        [Theory]
        [InlineData("abc", "abc")]
        [InlineData("", "\"\"")]
        [InlineData("a b", "\"a b\"")]
        [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
        [InlineData("C:\\dir with space\\", "\"C:\\dir with space\\\\\"")]
        [InlineData("set -e; base64 -d > /usr/local/bin/x.tmp", "\"set -e; base64 -d > /usr/local/bin/x.tmp\"")]
        public void QuoteArg_FollowsWindowsCommandLineRules(string input, string expected)
        {
            Assert.Equal(expected, Wsl.QuoteArg(input));
        }

        [Fact]
        public void Decode_Utf16FromWslExe()
        {
            var bytes = Encoding.Unicode.GetBytes("  NAME      STATE     VERSION\r\n* Ubuntu-24.04    Running    2\r\n");
            Assert.Contains("Ubuntu-24.04", Wsl.Decode(bytes));
        }

        [Fact]
        public void Decode_Utf8FromLinux()
        {
            var bytes = Encoding.UTF8.GetBytes("Modern traceroute for Linux, version 0.1.36 — ok");
            Assert.Equal("Modern traceroute for Linux, version 0.1.36 — ok", Wsl.Decode(bytes));
        }

        [Fact]
        public void Sha256Hex_IsLowercaseHex()
        {
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                Wsl.Sha256Hex(Encoding.ASCII.GetBytes("abc")));
        }
    }
}
