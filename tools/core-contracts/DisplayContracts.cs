using GEmuera.Core.Display;

namespace CoreContractSmoke;

/// <summary>
/// High-risk M2.0 contract helper. The existing smoke entry point is kept
/// untouched because this work package is limited to this new helper file.
/// Call <see cref="Run"/> from a harness when the M2 work package is enabled.
/// </summary>
public static class DisplayContracts
{
#if M2_STANDALONE
    public static int Main(string[] args)
    {
        Run();
        Console.WriteLine("M2 display contract smoke passed.");
        return 0;
    }
#endif

    public static void Run()
    {
        DeepCopyContract();
        OrderBarrierContract();
        DataOnlyContract();
    }

    private static void DeepCopyContract()
    {
        var sourceMargin = new[] { -4, 2, 3, 4 };
        var sourceMatrixRow = new[] { 1.0f, 0.5f };
        var sourceMetadata = new Dictionary<string, string> { ["legacy"] = "retained" };
        var sourceParts = new List<DisplayPart>
        {
            new DisplayImagePart(
                "normal.png",
                "pressed.png",
                new DisplayRect(-12, -7, 64, 18),
                style: new DisplayStyle(margin: sourceMargin, colorMatrix: new[] { sourceMatrixRow }, metadata: sourceMetadata),
                interaction: new DisplayInteraction(true, "choice", "42", "integer", true, generation: 9, pointXIsLocked: true, lockedX: -12)),
        };
        var sourceLines = new List<DisplayLine>
        {
            new("line-1", 1, parts: sourceParts, metadata: sourceMetadata),
        };
        var transaction = new DisplayTransaction(
            7,
            "tx-7",
            new DisplayEffectSequence(12, 14),
            sourceLines,
            new DisplayScroll(DisplayScrollIntent.KeepChoicesVisible, "line-1", -0.25, "scroll-source"),
            atomicVisibility: true,
            provenance: new DisplayProvenance("erb://fixture", "display", 3, 2, sourceMetadata));

        sourceMargin[0] = 999;
        sourceMatrixRow[0] = 999;
        sourceMetadata["legacy"] = "mutated";
        sourceParts.Clear();
        sourceLines.Clear();

        Check(transaction.AtomicVisibility, "Atomic visibility was not retained.");
        Check(transaction.Generation == 7 && transaction.TransactionId == "tx-7", "Transaction identity was not retained.");
        Check(transaction.EffectSequenceStart == 12 && transaction.EffectSequenceEnd == 14, "Effect sequence range was not retained.");
        Check(transaction.Lines.Count == 1 && transaction.Lines[0].Parts.Count == 1, "Transaction retained caller collections.");
        var image = (DisplayImagePart)transaction.Lines[0].Parts[0];
        Check(image.Src == "normal.png" && image.Srcb == "pressed.png", "src/srcb were not retained.");
        Check(image.Bounds.X == -12 && image.Bounds.Y == -7 && image.Interaction?.LockedX == -12, "Negative/locked coordinates were not retained.");
        Check(image.Style.Margin[0] == -4 && image.Style.ColorMatrix[0][0] == 1.0f, "Nested style collections were not copied.");
        Check(transaction.Provenance.Metadata["legacy"] == "retained", "Provenance metadata was not copied.");

        var deepCopy = transaction.DeepCopy();
        Check(deepCopy.CanonicalHash == transaction.CanonicalHash, "Repeated DTO copy changed canonical identity.");
        Check(!ReferenceEquals(deepCopy.Lines[0], transaction.Lines[0]), "DeepCopy returned a shared line object.");
        Check(!ReferenceEquals(deepCopy.Lines[0].Parts[0], transaction.Lines[0].Parts[0]), "DeepCopy returned a shared part object.");
    }

    private static void OrderBarrierContract()
    {
        var items = new DisplayTransactionItem[]
        {
            new DisplayLineItem(new DisplayLine("line", 1)),
            new DisplayBarrierItem(new DisplayOrderBarrier(DisplayBarrierKind.Commit, 20, "before wait")),
            new DisplayDataOnlyItem(new DisplayDataOnlyPatch("line", "button", generation: 3, effectSequence: 21)),
            new DisplayBarrierItem(new DisplayOrderBarrier(DisplayBarrierKind.Wait, 22, "input wait")),
        };
        var transaction = new DisplayTransaction(3, "ordered", new DisplayEffectSequence(20, 22), items);
        Check(transaction.Items.Count == 4, "Transaction item order was not retained.");
        Check(transaction.Items[1] is DisplayBarrierItem && transaction.Items[3] is DisplayBarrierItem, "Order barriers moved or disappeared.");
        Check(((DisplayBarrierItem)transaction.Items[3]).Barrier.Kind == DisplayBarrierKind.Wait, "Wait barrier kind changed.");
    }

    private static void DataOnlyContract()
    {
        var patch = new DisplayDataOnlyPatch("line", "choice", "new value", generation: 8);
        Check(!patch.RebuildsVisualTree, "Data-only patch claims to rebuild visual tree.");
        var transaction = new DisplayTransaction(8, "data-only", DisplayEffectSequence.Empty, new DisplayTransactionItem[] { new DisplayDataOnlyItem(patch) });
        Check(transaction.Lines.Count == 0 && transaction.DataOnlyPatches.Count == 1, "Data-only patch was projected as a visual line.");

        var observed = new RecordingObserver();
        new DisplayTee().Observe(transaction, observed);
        Check(observed.Transaction is not null, "Tee did not publish an observation snapshot.");
        Check(!ReferenceEquals(observed.Transaction, transaction), "Tee published the producer transaction by reference.");
        Check(observed.Transaction!.DataOnlyPatches[0].RebuildsVisualTree == false, "Tee changed data-only semantics.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class RecordingObserver : IDisplayTeeObserver
    {
        public DisplayTransaction? Transaction { get; private set; }

        public void Observe(DisplayTransaction transaction)
        {
            Transaction = transaction;
        }
    }
}
