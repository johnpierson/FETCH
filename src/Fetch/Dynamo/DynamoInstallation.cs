using System;

namespace Fetch.Dynamo
{
    /// <summary>The Dynamo version FETCH decided to target, and the folder its packages live in.</summary>
    internal sealed class DynamoInstallation
    {
        internal DynamoInstallation(Version version, string packagePath, DynamoPathConfidence confidence)
        {
            Version = version;
            PackagePath = packagePath;
            Confidence = confidence;
        }

        internal Version Version { get; }

        internal string PackagePath { get; }

        /// <summary>How strong the evidence is that Dynamo actually reads this folder.</summary>
        internal DynamoPathConfidence Confidence { get; }

        /// <summary>False when nothing was found on disk and the path is inferred.</summary>
        internal bool PackagePathExists => Confidence != DynamoPathConfidence.Guessed;
    }
}
