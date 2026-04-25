using Microsoft.AspNetCore.Http;
using System;

namespace Kartist.Helpers
{
    public static class FileUploadValidator
    {
        public static bool TryValidateImage(
            IFormFile file,
            long maxBytes,
            out string safeExtension,
            out string error)
        {
            safeExtension = null;
            error = null;

            if (file == null || file.Length == 0)
            {
                error = "Dosya boş.";
                return false;
            }

            if (file.Length > maxBytes)
            {
                long maxMb = maxBytes / (1024 * 1024);
                error = $"Dosya çok büyük. Maksimum {maxMb}MB.";
                return false;
            }

            if (string.IsNullOrEmpty(file.ContentType) ||
                !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                error = "Sadece resim dosyası yükleyebilirsiniz.";
                return false;
            }

            Span<byte> header = stackalloc byte[12];
            int read;
            using (var stream = file.OpenReadStream())
            {
                read = stream.Read(header);
            }

            if (read < 4)
            {
                error = "Geçersiz dosya formatı.";
                return false;
            }

            safeExtension = DetectExtension(header[..read]);
            if (safeExtension == null)
            {
                error = "Sadece JPG, PNG, GIF veya WEBP yükleyebilirsiniz.";
                return false;
            }

            return true;
        }

        private static string DetectExtension(ReadOnlySpan<byte> header)
        {
            if (header.Length >= 3 &&
                header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return ".jpg";

            if (header.Length >= 8 &&
                header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
                return ".png";

            if (header.Length >= 6 &&
                header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38 &&
                (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
                return ".gif";

            if (header.Length >= 12 &&
                header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
                return ".webp";

            return null;
        }
    }
}
