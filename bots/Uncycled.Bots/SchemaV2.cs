using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Sim.Match;

namespace Uncycled.Bots
{
    /// <summary>
    /// Fingerprint of the v2 network contract: the observation layout and constants, the policy action list and
    /// the public shape of the views the bots read. A model stores it; a model with another hash is refused.
    /// Nothing is left out (the v1 hash skips fields the v1 networks never read; v2 reads them).
    /// </summary>
    public static class SchemaV2
    {
        private static readonly Lazy<string> TextValue = new Lazy<string>(BuildText);
        private static readonly Lazy<string> HashValue = new Lazy<string>(ComputeHash);

        /// <summary>SHA-256 (lower-case hex) of <see cref="Text"/>.</summary>
        public static string Hash => HashValue.Value;

        public static string Text => TextValue.Value;

        private static string BuildText()
        {
            var sb = new StringBuilder();
            sb.Append(ObservationBuilderV2.SchemaText);
            foreach (string name in PolicyActionsV2.Names)
            {
                sb.Append("policy:").Append(name).Append('\n');
            }

            foreach (Type t in new[] { typeof(SeatView), typeof(MyView), typeof(CooldownView), typeof(GasView) })
            {
                foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    sb.Append("view:").Append(t.Name).Append('.').Append(p.Name).Append(':').Append(p.PropertyType.ToString()).Append('\n');
                }
            }

            return sb.ToString();
        }

        private static string ComputeHash()
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(Text));
                var hex = new StringBuilder(digest.Length * 2);
                foreach (byte b in digest)
                {
                    hex.Append(b.ToString("x2"));
                }

                return hex.ToString();
            }
        }
    }
}
