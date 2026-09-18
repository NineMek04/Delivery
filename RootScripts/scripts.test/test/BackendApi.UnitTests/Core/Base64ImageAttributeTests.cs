using System.ComponentModel.DataAnnotations;
using BackendApi.Core.Attributes;
using Xunit;

namespace BackendApi.UnitTests.Core
{
    public class Base64ImageAttributeTests
    {
        private readonly Base64ImageAttribute _attribute = new Base64ImageAttribute { MaxSizeBytes = 1024 * 1024 };

        [Fact]
        public void Validate_WhenNullOrEmpty_ReturnsSuccess()
        {
            var context = new ValidationContext(new object());
            
            Assert.Equal(ValidationResult.Success, _attribute.GetValidationResult(null, context));
            Assert.Equal(ValidationResult.Success, _attribute.GetValidationResult("", context));
            Assert.Equal(ValidationResult.Success, _attribute.GetValidationResult("   ", context));
        }

        [Theory]
        [InlineData("http://localhost:9000/delivery-media/items/123.jpg")]
        [InlineData("https://example.com/images/food.png")]
        public void Validate_WhenHttpOrHttpsUrl_ReturnsSuccess(string url)
        {
            var context = new ValidationContext(new object());
            var result = _attribute.GetValidationResult(url, context);

            Assert.Equal(ValidationResult.Success, result);
        }

        [Fact]
        public void Validate_WhenValidPngBase64_ReturnsSuccess()
        {
            // Minimal valid 1x1 PNG in base64
            var validPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
            var context = new ValidationContext(new object());

            var result = _attribute.GetValidationResult(validPngBase64, context);

            Assert.Equal(ValidationResult.Success, result);
        }

        [Fact]
        public void Validate_WhenDataUriPrefixIncluded_ReturnsSuccess()
        {
            var validPngDataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
            var context = new ValidationContext(new object());

            var result = _attribute.GetValidationResult(validPngDataUri, context);

            Assert.Equal(ValidationResult.Success, result);
        }

        [Fact]
        public void Validate_WhenNotBase64_ReturnsError()
        {
            var invalidBase64 = "This is definitely not a base64 string @#$%^&*!";
            var context = new ValidationContext(new object());

            var result = _attribute.GetValidationResult(invalidBase64, context);

            Assert.NotNull(result);
            Assert.Contains("Base64", result.ErrorMessage);
        }

        [Fact]
        public void Validate_WhenMagicBytesInvalid_ReturnsError()
        {
            // Valid base64 encoding of plain text "Hello World text file"
            var textBase64 = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Hello World text file masquerading as image"));
            var context = new ValidationContext(new object());

            var result = _attribute.GetValidationResult(textBase64, context);

            Assert.NotNull(result);
            Assert.Contains("ไม่รองรับ", result.ErrorMessage);
        }

        [Fact]
        public void Validate_WhenExceedsMaxSize_ReturnsError()
        {
            var strictAttr = new Base64ImageAttribute { MaxSizeBytes = 10 }; // 10 bytes max
            var validPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
            var context = new ValidationContext(new object());

            var result = strictAttr.GetValidationResult(validPngBase64, context);

            Assert.NotNull(result);
            Assert.Contains("เกินกำหนด", result.ErrorMessage);
        }
    }
}
