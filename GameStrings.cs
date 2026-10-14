using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace QoL.Util
{
    /// <summary>Converts between engine strings (<see cref="MioGame.String"/>) and managed strings.</summary>
    internal static unsafe class GameStrings
    {
        private static readonly ConcurrentDictionary<string, nint> constants = new();

        internal static string Read(MioGame.String* s)
        {
            if (s == null || s->data.data == null || s->size == 0) return "";
            return Encoding.UTF8.GetString(s->data.data, (int)s->size);
        }

        internal static string Read(ref MioGame.String s)
        {
            fixed (MioGame.String* p = &s) return Read(p);
        }

        /// <summary>
        /// An engine string for <paramref name="value"/>, in native memory that is allocated once
        /// per distinct value and never freed. That makes it safe to pass to game functions that
        /// keep the pointer (e.g. as a save-entry key). Marked static so the engine never frees it.
        /// </summary>
        internal static MioGame.String* Constant(string value)
        {
            return (MioGame.String*)constants.GetOrAdd(value, static v =>
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(v);
                byte* chars = (byte*)NativeMemory.Alloc((nuint)utf8.Length + 1);
                utf8.AsSpan().CopyTo(new Span<byte>(chars, utf8.Length));
                chars[utf8.Length] = 0;

                var s = (MioGame.String*)NativeMemory.AllocZeroed((nuint)sizeof(MioGame.String));
                s->data.data = chars;
                s->size = (uint)utf8.Length;
                s->is_static = 1;
                return (nint)s;
            });
        }
    }
}