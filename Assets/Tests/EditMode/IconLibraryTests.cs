using System.IO;
using NUnit.Framework;
using Safehouse.UI;
using UnityEngine;

namespace Safehouse.Tests
{
    /// <summary>
    /// Icon loading, with synthetic PNGs in a temp folder - the real icons are game art that is not in
    /// the repository, so nothing here depends on them being present.
    /// </summary>
    public sealed class IconLibraryTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "safehouse-icons-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, IconLibrary.Size.ToString()));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private void WritePng(string itemId, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                File.WriteAllBytes(Path.Combine(_root, IconLibrary.Size.ToString(), itemId + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void AnIconIsLoadedAtItsFileSize()
        {
            WritePng("rifle", 192, 48);
            var library = new IconLibrary(_root);

            var icon = library.Get("rifle");

            Assert.IsNotNull(icon);
            Assert.AreEqual((192, 48), (icon.width, icon.height));
            Object.DestroyImmediate(icon);
        }

        [Test]
        public void TextFreeArtIsReadFromItsOwnFolderAndNotMixedUpWithTheStashIcon()
        {
            WritePng("rifle", 192, 48);
            Directory.CreateDirectory(Path.Combine(_root, "art"));
            var art = new Texture2D(256, 65, TextureFormat.RGBA32, false);
            File.WriteAllBytes(Path.Combine(_root, "art", "rifle.png"), art.EncodeToPNG());
            Object.DestroyImmediate(art);
            var library = new IconLibrary(_root);

            var loadedArt = library.GetArt("rifle");
            var icon = library.Get("rifle");

            Assert.AreEqual((256, 65), (loadedArt.width, loadedArt.height));
            Assert.AreEqual((192, 48), (icon.width, icon.height));
            Assert.IsNull(library.GetArt("no-such-item"));
            Object.DestroyImmediate(loadedArt);
            Object.DestroyImmediate(icon);
        }

        [Test]
        public void AnItemWithoutAnIconGivesNull()
        {
            var library = new IconLibrary(_root);

            Assert.IsTrue(library.HasIcons);
            Assert.IsNull(library.Get("no-such-item"));
        }

        [Test]
        public void AMissingFolderIsNotAnError()
        {
            var library = new IconLibrary(Path.Combine(_root, "not-there"));

            Assert.IsFalse(library.HasIcons);
            Assert.IsNull(library.Get("rifle"));
        }

        [Test]
        public void TheSameIconIsNotLoadedTwice()
        {
            WritePng("rifle", 8, 8);
            var library = new IconLibrary(_root);

            var first = library.Get("rifle");

            Assert.AreSame(first, library.Get("rifle"));
            Object.DestroyImmediate(first);
        }

        [Test]
        public void AFileThatIsNotAnImageGivesNull()
        {
            File.WriteAllText(Path.Combine(_root, IconLibrary.Size.ToString(), "broken.png"), "not a png");
            var library = new IconLibrary(_root);

            Assert.IsNull(library.Get("broken"));
        }

        [Test]
        public void AnIconCloseToTheBoxShapeIsStretchedToFillIt()
        {
            // A 2x2 helmet (1.0) in the 96x108 slot card (0.89): about 12% off, invisible once stretched.
            Assert.AreEqual(ScaleMode.StretchToFill, IconLibrary.FitInto(1.0f, 96f / 108f));
            // A 3x4 armor (0.75) in the same card: about 19% off.
            Assert.AreEqual(ScaleMode.StretchToFill, IconLibrary.FitInto(0.75f, 96f / 108f));
        }

        [Test]
        public void AnIconFarFromTheBoxShapeIsFittedWholeNeverCropped()
        {
            // A 4x3 rig (1.33) in the 96x108 card: 50% off, so it would be visibly squashed.
            Assert.AreEqual(ScaleMode.ScaleToFit, IconLibrary.FitInto(4f / 3f, 96f / 108f));
            // A 4x1 rifle in a card of the same shape.
            Assert.AreEqual(ScaleMode.ScaleToFit, IconLibrary.FitInto(4f, 96f / 108f));
            // Either way round: a tall icon in a wide box.
            Assert.AreEqual(ScaleMode.ScaleToFit, IconLibrary.FitInto(0.5f, 4f));
        }

        [Test]
        public void ADegenerateShapeFallsBackToFitting()
        {
            Assert.AreEqual(ScaleMode.ScaleToFit, IconLibrary.FitInto(0f, 1f));
            Assert.AreEqual(ScaleMode.ScaleToFit, IconLibrary.FitInto(1f, 0f));
        }

        [Test]
        public void AnIdThatWouldLeaveTheIconFolderIsRefused()
        {
            var library = new IconLibrary(_root);

            Assert.IsNull(library.Get("../secret"));
            Assert.IsNull(library.Get("a/b"));
            Assert.IsNull(library.Get(""));
            Assert.IsNull(library.Get(null));
        }
    }
}
