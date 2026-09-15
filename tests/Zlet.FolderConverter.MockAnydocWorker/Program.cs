// Mock anydoc worker for handshake tests.
// Usage: zlet-mock-anydoc-worker <mode>
//
// Modes:
//   wrong-protocol-version  — emit handshake with version="2.0"
//   wrong-anydoc-version    — emit handshake with anydocVersion="0.1.0"
//   wrong-revision          — emit handshake with wrong anydocRevision
//   hang                    — sleep forever without emitting anything
//
// Any unknown / missing mode → exit 1.

var mode = args.Length > 0 ? args[0] : "";

switch (mode)
{
    case "wrong-protocol-version":
        Console.WriteLine(
            """{"ready":true,"version":"2.0","anydocVersion":"0.2.4","anydocRevision":"42bf1c5ecdde9eb0d96d6bd75a9e6698cf93b14c"}""");
        Console.Out.Flush();
        // Keep stdin open so the runner doesn't see EOF prematurely
        Console.In.ReadToEnd();
        break;

    case "wrong-anydoc-version":
        Console.WriteLine(
            """{"ready":true,"version":"1.0","anydocVersion":"0.1.0","anydocRevision":"42bf1c5ecdde9eb0d96d6bd75a9e6698cf93b14c"}""");
        Console.Out.Flush();
        Console.In.ReadToEnd();
        break;

    case "wrong-revision":
        Console.WriteLine(
            """{"ready":true,"version":"1.0","anydocVersion":"0.2.4","anydocRevision":"0000000000000000000000000000000000000000"}""");
        Console.Out.Flush();
        Console.In.ReadToEnd();
        break;

    case "hang":
        // Never emit a handshake — causes the 5-second timeout to fire
        Thread.Sleep(Timeout.Infinite);
        break;

    default:
        Console.Error.WriteLine($"Unknown mode: {mode}");
        Environment.Exit(1);
        break;
}
