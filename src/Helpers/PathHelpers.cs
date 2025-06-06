using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DLSS_Swapper.Helpers
{
    internal static class PathHelpers
    {
        public static char[] InvalidFileNamePathChars { get; private set; }

        static PathHelpers()
        {
            var invalidChars = new List<char>();
            invalidChars.AddRange(Path.GetInvalidFileNameChars());
            invalidChars.AddRange(Path.GetInvalidPathChars());
            invalidChars.Add('.');
            InvalidFileNamePathChars = invalidChars.Distinct().ToArray();
        }


        /// <summary>
        /// Tries to format path on disk so any and all paths will match after they have gone through this method.  
        /// </summary>
        /// <param name="path">Path on local disk</param>
        /// <returns>Formatted path on disk</returns>
        internal static string NormalizePath(string path)
        {
            // Via https://stackoverflow.com/a/21058152
            //new Uri(path).LocalPath
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

    }
}
