using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DbUp.Engine;
using DbUp.Engine.Transactions;

namespace DbUp.ScriptProviders
{
    ///<summary>
    /// Alternate <see cref="IScriptProvider"/> implementation which retrieves upgrade scripts from version folders in a directory.
    ///</summary>
    public class VersionFoldersScriptProvider : IScriptProvider
    {
        private readonly string directoryPath;
        private readonly Encoding encoding;
        private readonly Func<string, bool> filter;
        private readonly string targetVersion;
        private FileSystemScriptOptions options;

        ///<param name="directoryPath">Path to SQL upgrade scripts</param>
        public VersionFoldersScriptProvider(string directoryPath) : 
            this(directoryPath, null, new FileSystemScriptOptions())
        {
        }

        ///<param name="directoryPath">Path to SQL upgrade scripts</param>
        ///<param name="targetVersion">Exclude scripts in subfolders with a higher version number.</param>
        public VersionFoldersScriptProvider(string directoryPath, string targetVersion) :
            this(directoryPath, targetVersion, new FileSystemScriptOptions())
        {
        }

        ///<param name="directoryPath">Path to SQL upgrade scripts</param>
        ///<param name="filter">The filter.</param>
        [Obsolete("Use the constructor with Options argument instead")]
        public VersionFoldersScriptProvider(string directoryPath, Func<string, bool> filter) :
            this(directoryPath, null, new FileSystemScriptOptions() { Filter = filter })
        {
        }

        ///<param name="directoryPath">Path to SQL upgrade scripts</param>
        ///<param name="encoding">The encoding.</param>
        ///<param name="targetVersion">Exclude scripts in subfolders with a higher version number.</param>
        [Obsolete("Use the constructor with Options argument instead")]
        public VersionFoldersScriptProvider(string directoryPath, Encoding encoding, string targetVersion) :
            this(directoryPath, targetVersion, new FileSystemScriptOptions() { Encoding = encoding })
        {
        }

        ///<param name="directoryPath">Path to SQL upgrade scripts</param>
        ///<param name="encoding">The encoding.</param>
        ///<param name="filter">The filter.</param>
        [Obsolete("Use the constructor with Options argument instead")]
        public VersionFoldersScriptProvider(string directoryPath, Encoding encoding, Func<string, bool> filter) :
            this(directoryPath, null, new FileSystemScriptOptions() { Filter = filter })
        {
        }

        ///<param name="directoryPath">Path to SQL upgrade scripts</param>
        ///<param name="filter">The filter.</param>
        ///<param name="targetVersion">Exclude scripts in subfolders with a higher version number.</param>
        [Obsolete("Use the constructor with Options argument instead")]
        public VersionFoldersScriptProvider(string directoryPath, Func<string, bool> filter, string targetVersion) :
            this(directoryPath, targetVersion, new FileSystemScriptOptions() { Filter = filter })
        {
        }

        ///<param name="directoryPath">Path to SQL upgrade scripts</param>
        ///<param name="encoding">The encoding.</param>
        ///<param name="filter">The filter.</param>
        ///<param name="targetVersion">Exclude scripts in subfolders with a higher version number.</param>
        [Obsolete("Use the constructor with Options argument instead")]
        public VersionFoldersScriptProvider(string directoryPath, Encoding encoding, Func<string, bool> filter, string targetVersion) :
            this(directoryPath, targetVersion, new FileSystemScriptOptions() { Filter = filter })
        {
        }

        ///<summary>
        ///</summary>
        ///<param name="directoryPath">Path to SQL upgrade scripts</param>
        ///<param name="options">Different options for the file system script provider</param>
        ///<param name="targetVersion">Exclude scripts in subfolders with a higher version number.</param>
        public VersionFoldersScriptProvider(string directoryPath, string targetVersion, FileSystemScriptOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            this.directoryPath = directoryPath;
            this.targetVersion = targetVersion;
            this.filter = options.Filter;
            this.encoding = options.Encoding;
            this.options = options;
        }

        /// <exception cref="InvalidOperationException">Thrown when multiple subfolder names parse to the same version number.</exception>
        public IEnumerable<SqlScript> GetScripts(IConnectionManager connectionManager)
        {
            return string.IsNullOrEmpty(targetVersion) ?
                GetScriptsWithoutTargetVersion() :
                GetScriptsWithTargetVersion();
        }

        private IEnumerable<SqlScript> GetScriptsWithoutTargetVersion()
        {
            var scripts = new List<SqlScript>();

            foreach (var folderPath in Directory.GetDirectories(directoryPath))
            {
                var folderName = new DirectoryInfo(folderPath).Name;
                scripts.AddRange(GetScriptsFromFolder(folderName));
            }

            return scripts;
        }

        /// <summary>
        /// Excludes scripts from version folders with a version higher than target version.
        /// A <see cref="filter"/> must be supplied for folders to exclude.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when an unparseable folder version is encountered, or when multiple subfolder names parse to the same version number.</exception>
        private IEnumerable<SqlScript> GetScriptsWithTargetVersion()
        {
            var folderNames = 
                Directory.GetDirectories(directoryPath)
                    .Select(d => new DirectoryInfo(d).Name);

            // filter on folder names
            if (filter != null)
            {
                folderNames = folderNames.Where(filter);
            }

            var scripts = new List<SqlScript>();

            if (folderNames.Any())
            {
                Version target = ParseVersion(targetVersion);
                var parsedVersions = new List<Version>();

                foreach (var folderName in folderNames)
                {
                    // Expecting all encountered folder names to be parseable. 
                    var folderVersion = ParseVersion(folderName);

                    if (folderVersion <= target)
                    {
                        if (parsedVersions.Contains(folderVersion))
                        {
                            throw new InvalidOperationException(string.Format("Version '{0}' parsed for folder '{1}' is ambiguous.", folderVersion, folderName));
                        }

                        scripts.AddRange(GetScriptsFromFolder(folderName));
                        parsedVersions.Add(folderVersion);
                    }
                }
            }

            return scripts;
        }

        /// <summary>
        /// Get scripts from the specified version folder. 
        /// The version folder name is prefixed to the scriptname to make scripts with duplicate names unique when from different folders.
        /// </summary>
        /// <param name="folder">name of subfolder within directory path</param>
        private IEnumerable<SqlScript> GetScriptsFromFolder(string folder)
        {
            var absoluteFolderPath = Path.Combine(directoryPath, folder);

            var folderedFileNames = Directory.GetFiles(absoluteFolderPath, "*.sql")
                .Select(f => Path.Combine(folder, new FileInfo(f).Name))
                .AsEnumerable();

            // filter on folder\file combination
            if (this.filter != null)
            {
                folderedFileNames = folderedFileNames.Where(filter);
            }

            // load file contents
            var sqlScripts = new List<SqlScript>();

            foreach (var folderedFileName in folderedFileNames)
            {
                var filePath = Path.Combine(directoryPath, folderedFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    sqlScripts.Add(SqlScript.FromStream(folderedFileName, fileStream, encoding));
                }
            }

            return sqlScripts;
        }

        private static Version ParseVersion(string str)
        {
            Version parsed;

            if (!TryParseVersion(str, out parsed))
            {
                throw new InvalidOperationException(string.Format("Error parsing version from string '{0}'.", str));
            }
            return parsed;
        }

        private static bool TryParseVersion(string str, out Version parsed)
        {
            // Find at least 1 and max 4 delimited decimals at string start, otherwise fail the entire match.
            var regex = new Regex(@"^(?>0*(\d+)(?:[\^_\-\.,~ ]+0*(\d+))?(?:[\^_\-\.,~ ]+0*(\d+))?(?:[\^_\-\.,~ ]+0*(\d+))?)(?![\^_\-\.,~ ]+\d+)");
            var result = regex.Match(str);

            if (result.Success)
            {
                int val;
                int major = int.Parse(result.Groups[ 1 ].Value);
                int minor = int.TryParse(result.Groups[ 2 ].Value, out val) ? val : 0;
                int build = int.TryParse(result.Groups[ 3 ].Value, out val) ? val : 0;
                int revision = int.TryParse(result.Groups[ 4 ].Value, out val) ? val : 0;

                parsed = new Version(major, minor, build, revision);
            }
            else
            {
                parsed = new Version();
            }

            return result.Success;
        }
    }
}