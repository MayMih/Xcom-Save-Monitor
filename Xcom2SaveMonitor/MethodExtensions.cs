using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xcom2SaveMonitor
{
    internal class MethodExtensions
    {

    }

    // Добавьте этот статический класс расширения в конец файла Program.cs или в отдельный файл, если требуется.
    internal static class StringExtensions
    {
        /// <summary>
        /// Возвращает подстроку после первого вхождения указанного символа.
        /// Если символ не найден — возвращает пустую строку.
        /// </summary>
        public static string StrAfter(this string source, char separator)
        {
            if (string.IsNullOrEmpty(source))
                return string.Empty;
            int idx = source.IndexOf(separator);
            if (idx < 0 || idx + 1 >= source.Length)
                return string.Empty;
            return source.Substring(idx + 1);
        }

        /// <summary>
        /// Преобразует строку в int. Если не удалось — возвращает 0.
        /// </summary>
        public static int? ToInt(this string source)
        {
            if (int.TryParse(source, out int result))
            {
                return result;
            }
            return null;
        }
    }
}
