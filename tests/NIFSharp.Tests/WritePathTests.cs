using NIFSharp;
using Xunit;

namespace NIFSharp.Tests
{
    /// <summary>
    /// That writing lays a block out the same way reading expects it.
    /// </summary>
    /// <remarks>
    /// Reading is sequential: it sizes each array from its length expression as it
    /// reaches it, and re-tests each condition against the fields it has just read.
    /// Writing has to do both or the two disagree, and because nothing resynchronises
    /// between blocks, a block written even one byte short takes every block after it
    /// down with it.
    ///
    /// These are the two ways that used to happen — a count with no elements behind
    /// it, and a condition decided before the field it names was set.
    /// </remarks>
    public class WritePathTests
    {
        private static readonly NifXmlDatabase Db = NifXmlDatabase.LoadEmbedded();

        /// <summary>Saves and reads back, which is where a layout disagreement shows.</summary>
        private static NifModel RoundTrip(NifModel model)
        {
            model.SetRoots([model.Blocks[0]]);
            model.UpdateHeader();

            using var stream = new MemoryStream();
            model.Save(stream);
            stream.Position = 0;

            return NifModel.Load(stream, Db);
        }

        /// <summary>A model with a named node at each end, to catch a desync.</summary>
        private static NifModel Build(out NifItem middle, string middleType)
        {
            NifModel model = NifModel.CreateNew(Db, bsVersion: 100);

            NifItem root = model.InsertBlock("NiNode");
            model.SetString(root, "Name", "root");

            middle = model.InsertBlock(middleType);

            NifItem tail = model.InsertBlock("NiNode");
            model.SetString(tail, "Name", "tail");

            return model;
        }

        /// <summary>
        /// A block edited after the header was measured is still written at the size the
        /// header states.
        /// </summary>
        /// <remarks>
        /// The game reads a block and checks it consumed exactly the bytes the header
        /// promised. A texture path rewritten to where the texture now is -- sixteen bytes
        /// longer -- left the header saying 116 for a block that wrote 132, and the file
        /// loaded in every viewer and was refused by the game and the Creation Kit with
        /// "stream size mismatch". Saving measures the header, so no caller has to know.
        /// </remarks>
        [Fact]
        public void ABlockEditedAfterMeasuringIsStillTheSizeTheHeaderSays()
        {
            // A texture set, because its paths are written into the block itself rather than
            // pooled in the header the way a node's name is.
            const string Short = @"textures\short.dds";
            const string Where = @"textures\actors\housecat\catsimple_albedo_5.dds";

            NifModel model = Build(out NifItem set, "BSShaderTextureSet");
            model.SetRoots([model.Blocks[0]]);
            model.SetArraySize(set, "Num Textures", "Textures", 2);
            NifItem textures = model.FindItem(set, "Textures")!;
            textures.Children[0].Value.Set(Short);
            model.UpdateHeader();

            int[] measured = [.. model.FindItem(model.Header, "Block Size")!.Children.Select(c => (int)c.Value.ToUInt())];

            // The edit the header knows nothing about: the path the texture ended up at.
            textures.Children[0].Value.Set(Where);

            using var stream = new MemoryStream();
            model.Save(stream);
            stream.Position = 0;

            NifModel back = NifModel.Load(stream, Db);
            int[] declared = [.. back.FindItem(back.Header, "Block Size")!.Children.Select(c => (int)c.Value.ToUInt())];

            Assert.Equal(measured[1] + (Where.Length - Short.Length), declared[1]);
            Assert.Equal("tail", back.GetName(back.Blocks[2]));
            Assert.Equal(Where, back.FindItem(back.Blocks[1], "Textures")!.Children[0].Value.Get<string>());
        }

        [Fact]
        public void CountWithoutElementsIsStillWrittenInFull()
        {
            NifModel model = Build(out NifItem node, "NiNode");

            // A count set on its own, which is the state a writer leaves behind
            // whenever it sets a count and forgets the array.
            model.FindItem(node, "Num Children")!.Value.SetCount(2);

            NifModel reloaded = RoundTrip(model);

            NifItem written = reloaded.Blocks[1];

            Assert.Equal(2u, reloaded.GetUInt(written, "Num Children"));
            Assert.Equal(2, reloaded.FindItem(written, "Children")!.Children.Count);
        }

        [Fact]
        public void ShortArrayDoesNotDesyncTheBlocksAfterIt()
        {
            NifModel model = Build(out NifItem node, "NiNode");

            model.FindItem(node, "Num Children")!.Value.SetCount(2);

            NifModel reloaded = RoundTrip(model);

            // The real cost of a short block is paid by its neighbours: the reader
            // carries on from the wrong offset and misreads everything after.
            Assert.Equal(3, reloaded.Blocks.Count);
            Assert.Equal("tail", reloaded.GetName(reloaded.Blocks[2]));
        }

        [Fact]
        public void ConditionIsRetestedAgainstTheFieldItNames()
        {
            NifModel model = Build(out NifItem partition, "NiSkinPartition");

            model.SetArraySize(partition, "Num Partitions", "Partitions", 1);
            NifItem entry = model.FindItem(partition, "Partitions")!.Children[0];

            // Sizing decided, and cached, that the map is present.
            model.FindItem(entry, "Has Vertex Map")!.Value.SetCount(1);
            model.SetArraySize(entry, "Num Vertices", "Vertex Map", 3);

            Assert.Equal(3, model.FindItem(entry, "Vertex Map")!.Children.Count);

            // Withdrawing it afterwards has to withdraw the array with it, cached
            // answer or not.
            model.FindItem(entry, "Has Vertex Map")!.Value.SetCount(0);

            NifModel reloaded = RoundTrip(model);

            NifItem written = reloaded.FindItem(reloaded.Blocks[1], "Partitions")!.Children[0];

            Assert.Equal(0u, reloaded.GetUInt(written, "Has Vertex Map"));
            Assert.Equal(3u, reloaded.GetUInt(written, "Num Vertices"));

            // ...and again, the block after it is the one that would have suffered.
            Assert.Equal("tail", reloaded.GetName(reloaded.Blocks[2]));
        }

        [Fact]
        public void RecordedBlockSizeMatchesWhatIsWritten()
        {
            NifModel model = Build(out NifItem node, "NiNode");

            model.FindItem(node, "Num Children")!.Value.SetCount(2);

            NifModel reloaded = RoundTrip(model);

            // The header's sizes are measured before the bytes are produced, so a
            // resize during writing would leave them describing a block that no
            // longer exists. A reader that skips by them lands mid-field.
            using var again = new MemoryStream();
            reloaded.UpdateHeader();
            reloaded.Save(again);

            again.Position = 0;
            NifModel twice = NifModel.Load(again, Db);

            Assert.Equal(
                reloaded.FindItem(reloaded.Header, "Block Size")!.Children.Select(c => c.Value.ToUInt()),
                twice.FindItem(twice.Header, "Block Size")!.Children.Select(c => c.Value.ToUInt()));
        }

        [Fact]
        public void SavingTwiceProducesTheSameBytes()
        {
            NifModel model = NifModel.Load(
                Path.Combine(AppContext.BaseDirectory, "Resources", "nifly", "TestNifFile_Skinned_SE.nif"), Db);

            model.UpdateHeader();

            using var first = new MemoryStream();
            model.Save(first);

            first.Position = 0;
            NifModel reloaded = NifModel.Load(first, Db);
            reloaded.UpdateHeader();

            using var second = new MemoryStream();
            reloaded.Save(second);

            // Writing that resizes as it goes has to settle: if a second pass over
            // the same data produced different bytes, the layout would depend on how
            // many times the file had been through the tool.
            Assert.Equal(first.ToArray(), second.ToArray());
        }
    }
}
