using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KsfCompanion.Ui
{
    /// <summary>
    /// Downloads map previews (from ksf.surf) and avatars once, shrinks them and keeps them in
    /// %LOCALAPPDATA%\KSF Companion\images so the dashboard stays quick.
    /// </summary>
    sealed class ImageCache
    {
        const int StoredWidth = 1600;
        readonly HttpClient http;
        readonly string dir;
        readonly SemaphoreSlim downloads = new SemaphoreSlim(3, 3);

        public ImageCache(HttpClient http)
        {
            this.http = http;
            dir = Path.Combine(Program.CacheDir, "images");
            Directory.CreateDirectory(dir);
        }

        public Task<BitmapSource> MapAsync(string map, int width) => LoadAsync("map_" + map.ToLowerInvariant(), KsfApi.MapImage(map), width);

        public Task<BitmapSource> AvatarAsync(string url) => LoadAsync("avatar_" + Hash(url), url, 96);

        /// <summary>The map picture shrunk to a few dozen pixels and blurred: a soft wash of its colours for behind the dashboard.</summary>
        public async Task<BitmapSource> AmbientAsync(string map)
        {
            var small = await MapAsync(map, 40).ConfigureAwait(false);
            return small == null ? null : await Task.Run(() => Blur(small)).ConfigureAwait(false);
        }

        static BitmapSource Blur(BitmapSource source)
        {
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = bgra.PixelWidth, height = bgra.PixelHeight, stride = width * 4;
            var pixels = new byte[height * stride];
            bgra.CopyPixels(pixels, stride, 0);
            var temp = new byte[pixels.Length];
            // Three box blurs in a row come out close to a gaussian.
            for (var pass = 0; pass < 3; pass++)
            {
                BoxBlur(pixels, temp, width, height, 3, 1, 0);
                BoxBlur(temp, pixels, width, height, 3, 0, 1);
            }
            var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }

        static void BoxBlur(byte[] source, byte[] target, int width, int height, int radius, int dx, int dy)
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            for (var c = 0; c < 4; c++)
            {
                int sum = 0, count = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    int sx = x + k * dx, sy = y + k * dy;
                    if (sx < 0 || sy < 0 || sx >= width || sy >= height) continue;
                    sum += source[(sy * width + sx) * 4 + c];
                    count++;
                }
                target[(y * width + x) * 4 + c] = (byte)(sum / count);
            }
        }

        async Task<BitmapSource> LoadAsync(string key, string url, int width)
        {
            var file = Path.Combine(dir, key + ".jpg");
            var missing = file + ".missing";
            if (!File.Exists(file))
            {
                // Don't ask again for a day when a map has no preview.
                if (File.Exists(missing) && File.GetLastWriteTimeUtc(missing) > DateTime.UtcNow.AddDays(-1)) return null;

                await downloads.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!File.Exists(file))
                    {
                        using var response = await http.GetAsync(url).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            File.WriteAllText(missing, "");
                            return null;
                        }
                        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        await Task.Run(() => Store(bytes, file)).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException || ex is NotSupportedException)
                {
                    return null;
                }
                finally
                {
                    downloads.Release();
                }
            }

            return await Task.Run(() => Decode(file, width)).ConfigureAwait(false);
        }

        static void Store(byte[] bytes, string file)
        {
            BitmapSource image;
            using (var stream = new MemoryStream(bytes))
            {
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                image = frame.PixelWidth > StoredWidth
                    ? new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(StoredWidth / (double)frame.PixelWidth, StoredWidth / (double)frame.PixelWidth))
                    : (BitmapSource)frame;
            }
            var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
            encoder.Frames.Add(BitmapFrame.Create(image));
            var temp = file + ".tmp";
            using (var output = File.Create(temp)) encoder.Save(output);
            if (File.Exists(file)) File.Delete(file);
            File.Move(temp, file);
        }

        static BitmapSource Decode(string file, int width)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.DecodePixelWidth = width;
                image.UriSource = new Uri(file);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is FileFormatException)
            {
                try { File.Delete(file); } catch (IOException) { }
                return null;
            }
        }

        static string Hash(string text)
        {
            using var sha = SHA1.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            return BitConverter.ToString(bytes, 0, 8).Replace("-", "").ToLowerInvariant();
        }
    }
}
