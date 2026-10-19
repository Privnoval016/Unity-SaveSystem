using System;
using System.IO;
using UnityEngine;

namespace SaveSystem.Tests
{
    /** <summary>A fresh folder under the project's Temp folder for one test, deleted afterwards.</summary> */
    public sealed class TestFolder : IDisposable
    {
        public string Root { get; }

        public TestFolder(string name = "saves")
        {
            Root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "SaveSystemTests", Guid.NewGuid().ToString("N"), name));
        }

        public string FileIn(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            var parent = Directory.GetParent(Root)?.FullName;
            if (parent == null || !Directory.Exists(parent)) return;
            foreach (var file in Directory.GetFiles(parent, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(parent, true);
        }
    }
}
