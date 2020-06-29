using System.IO;

namespace Utiliread.Caching.Redis.Scripts
{
    internal class LuaScripts
    {
        // KEYS[1] The key
        // ARGV[1] Now unix milliseconds timestamp
        public readonly string Get = ReadScript("Get.lua");

        // KEYS[1] The key
        // ARGV[1] Now unix milliseconds timestamp
        // ARGV[2] Absolute expiration unix timestamp in milliseconds, -1 if none
        // ARGV[3] Sliding expiration in milliseconds, -1 if none
        // ARGV[4] The data
        public readonly string Set = ReadScript("Set.lua");

        // KEYS[1] The key
        // ARGV[1] Now unix milliseconds timestamp
        public readonly string Refresh = ReadScript("Refresh.lua");

        // KEYS[1] The key
        public readonly string Remove = ReadScript("Remove.lua");

        // KEYS[1..N-1] The tags to add
        // KEYS[N]      The key to tag
        public readonly string Tag = ReadScript("Tag.lua");

        // KEYS[1..N] The tags to invalidate
        public readonly string Invalidate = ReadScript("Invalidate.lua");

        public LuaScripts(string prefix)
        {
            Get = ReadScript("Get.lua").Replace("_expires-at_", $"{prefix}_expires-at_");
            Set = ReadScript("Set.lua").Replace("_expires-at_", $"{prefix}_expires-at_");
            Refresh = ReadScript("Refresh.lua").Replace("_expires-at_", $"{prefix}_expires-at_");
            Remove = ReadScript("Remove.lua").Replace("_expires-at_", $"{prefix}_expires-at_");
            Tag = ReadScript("Tag.lua").Replace("_expires-at_", $"{prefix}_expires-at_");
            Invalidate = ReadScript("Invalidate.lua").Replace("_expires-at_", $"{prefix}_expires-at_");
        }

        private static string ReadScript(string filename)
        {
            using var stream = typeof(LuaScripts).Assembly.GetManifestResourceStream(typeof(LuaScripts), filename);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
