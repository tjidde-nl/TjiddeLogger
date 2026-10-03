using FluentAssertions;
using Tjidde.Logging.Masking;
using Xunit;

namespace Tjidde.Logging.Tests;

public sealed class SensitiveDataMaskerTests
{
    private readonly SensitiveDataMasker _masker = new();

    [Theory]
    [InlineData("password")]
    [InlineData("Password")]
    [InlineData("PASSWORD")]
    [InlineData("wachtwoord")]
    [InlineData("token")]
    [InlineData("accesstoken")]
    [InlineData("access_token")]
    [InlineData("refreshtoken")]
    [InlineData("refresh_token")]
    [InlineData("secret")]
    [InlineData("clientsecret")]
    [InlineData("client_secret")]
    [InlineData("apikey")]
    [InlineData("api_key")]
    [InlineData("x-api-key")]
    [InlineData("authorization")]
    [InlineData("bearer")]
    [InlineData("cookie")]
    [InlineData("set-cookie")]
    public void IsSensitiveKey_ReturnsTrue_ForKnownSensitiveKeys(string key)
    {
        _masker.IsSensitiveKey(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("username")]
    [InlineData("email")]
    [InlineData("customerId")]
    [InlineData("orderId")]
    public void IsSensitiveKey_ReturnsFalse_ForNonSensitiveKeys(string key)
    {
        _masker.IsSensitiveKey(key).Should().BeFalse();
    }

    [Fact]
    public void MaskMessage_MasksPasswordInPlainMessage()
    {
        var message = "User login with password=SuperSecret123";
        var result = _masker.MaskMessage(message);
        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("SuperSecret123");
    }

    [Fact]
    public void MaskMessage_MasksTokenWithColonSeparator()
    {
        var message = "Received token: eyJhbGciOiJIUzI1NiJ9";
        var result = _masker.MaskMessage(message);
        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("eyJhbGciOiJIUzI1NiJ9");
    }

    [Fact]
    public void MaskMessage_ReturnsOriginal_WhenNoSensitiveDataPresent()
    {
        var message = "User 42 placed order 99";
        var result = _masker.MaskMessage(message);
        result.Should().Be(message);
    }

    [Fact]
    public void MaskMessage_HandlesEmptyString()
    {
        _masker.MaskMessage(string.Empty).Should().Be(string.Empty);
    }

    [Fact]
    public void MaskValue_ReturnsMasked_ForSensitiveKey()
    {
        var result = _masker.MaskValue("password", "hunter2");
        result.Should().Be("[REDACTED]");
    }

    [Fact]
    public void MaskValue_ReturnsOriginal_ForNonSensitiveKey()
    {
        var result = _masker.MaskValue("username", "john.doe");
        result.Should().Be("john.doe");
    }

    [Fact]
    public void MaskDictionary_MasksSensitiveValues()
    {
        var properties = new List<KeyValuePair<string, object?>>
        {
            new("username", "john.doe"),
            new("password", "hunter2"),
            new("token", "abc123"),
            new("orderId", "999")
        };

        var result = _masker.MaskDictionary(properties);

        result["username"].Should().Be("john.doe");
        result["password"].Should().Be("[REDACTED]");
        result["token"].Should().Be("[REDACTED]");
        result["orderId"].Should().Be("999");
    }

    [Fact]
    public void MaskDictionary_HandlesNullValues()
    {
        var properties = new List<KeyValuePair<string, object?>>
        {
            new("username", null),
            new("password", null)
        };

        var result = _masker.MaskDictionary(properties);

        result["username"].Should().Be(string.Empty);
        result["password"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void AdditionalSensitiveKeys_AreRespected()
    {
        var masker = new SensitiveDataMasker("[REDACTED]", ["internalCode", "tenantKey"]);

        masker.IsSensitiveKey("internalCode").Should().BeTrue();
        masker.IsSensitiveKey("tenantKey").Should().BeTrue();
        masker.IsSensitiveKey("orderId").Should().BeFalse();
    }

    [Fact]
    public void CustomPlaceholder_IsUsed()
    {
        var masker = new SensitiveDataMasker("***");
        var result = masker.MaskValue("password", "secret");
        result.Should().Be("***");
    }

    [Fact]
    public void MaskMessage_MasksSpaceSeparatedPassword_WhenValueIsLastWord()
    {
        var message = "password sdkljfsdkljf";
        var result = _masker.MaskMessage(message);
        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("sdkljfsdkljf");
    }

    [Fact]
    public void MaskMessage_DoesNotMask_WhenPasswordIsFollowedByNaturalLanguage()
    {
        var message = "password is missing";
        var result = _masker.MaskMessage(message);
        result.Should().Be(message);
    }

    [Theory]
    [InlineData("Authorization: Bearer abc.def.ghi", "Authorization: Bearer [REDACTED]")]
    [InlineData("authorization=Bearer abc.def.ghi", "authorization=Bearer [REDACTED]")]
    [InlineData("Proxy-Authorization: Basic dXNlcjpwYXNz", "Proxy-Authorization: Basic [REDACTED]")]
    [InlineData("Authorization: Bearer", "Authorization: [REDACTED]")]
    [InlineData("{\"user\":\"bob\",\"password\":\"hunter2\"}", "{\"user\":\"bob\",\"password\":\"[REDACTED]\"}")]
    [InlineData("password=\"my secret pass\" for bob", "password=\"[REDACTED]\" for bob")]
    [InlineData("{\"password\":\"pa\\\"ss word\"}", "{\"password\":\"[REDACTED]\"}")]
    public void MaskMessage_MasksTheCompleteValue(string message, string expected)
    {
        _masker.MaskMessage(message).Should().Be(expected);
    }

    [Fact]
    public void BlankKeys_AreIgnored()
    {
        var masker = new SensitiveDataMasker("[REDACTED]", ["", "  "], dynamicKeys: [""]);

        masker.MaskMessage("Order: 42, status=paid").Should().Be("Order: 42, status=paid");
    }

    [Fact]
    public void MaskMessage_HidesTheWholeMessage_WhenMaskingTimesOut()
    {
        var masker = new SensitiveDataMasker("[REDACTED]", null, null, null, TimeSpan.FromTicks(1));
        var message = string.Concat(Enumerable.Repeat("password=hunter2 ", 100_000));

        masker.MaskMessage(message).Should().Be("[REDACTED]");
    }

    [Fact]
    public void RuntimeKeys_ChangesApplyToAnExistingMasker()
    {
        var accessor = new FakeMaskedKeysAccessor();
        var masker = new SensitiveDataMasker("[REDACTED]", null, accessor);

        masker.MaskMessage("code SuperSecret42").Should().Be("code SuperSecret42");

        accessor.Keys = ["SuperSecret42"];
        masker.MaskMessage("code SuperSecret42").Should().Be("code [REDACTED]");
        masker.IsSensitiveKey("SuperSecret42").Should().BeTrue();

        accessor.Keys = [];
        masker.MaskMessage("code SuperSecret42").Should().Be("code SuperSecret42");
        masker.IsSensitiveKey("SuperSecret42").Should().BeFalse();
    }

    [Theory]
    [InlineData("Order processed successfully")]
    [InlineData("Tokens of appreciation")]
    [InlineData("caf\u00e9 au lait, no keys here")]
    public void MaskMessage_LeavesTextWithoutSensitiveValuesUnchanged(string message)
    {
        _masker.MaskMessage(message).Should().Be(message);
    }

    [Fact]
    public void MaskMessage_MasksKeyWrittenWithNonAsciiCaseVariant()
    {
        // The Kelvin sign matches "k" case-insensitively, so the quick key check must not skip this text.
        _masker.MaskMessage("to\u212Aen=abc123").Should().Be("to\u212Aen=[REDACTED]");
    }

    [Fact]
    public void MaskMessage_MasksAsciiTextForNonAsciiKey()
    {
        var masker = new SensitiveDataMasker(additionalKeys: ["\u212Aeycode"]);

        masker.MaskMessage("keycode=abc123").Should().Be("keycode=[REDACTED]");
    }

    [Fact]
    public void MaskMessage_MasksEveryKey_WhenPlaceholderIsNonAscii()
    {
        var masker = new SensitiveDataMasker(placeholder: "\u2588\u2588");

        masker.MaskMessage("password=abc token=def").Should().Be("password=\u2588\u2588 token=\u2588\u2588");
    }

    private sealed class FakeMaskedKeysAccessor : IMaskedKeysAccessor
    {
        public IReadOnlyCollection<string> Keys { get; set; } = [];

        public IReadOnlyCollection<string> GetKeys() => Keys;
    }
}
