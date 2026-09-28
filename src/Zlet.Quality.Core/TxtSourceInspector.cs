using System.Security.Cryptography;
using System.Text;

namespace Zlet.Quality.Core;

/// <summary>Native deterministic UTF text inspector. Line boundaries are preserved; no document semantics are inferred.</summary>
public sealed class TxtSourceInspector : ISourceInspector
{
    public const string Id="zlet-txt-native"; public const string Version="0.1.0";
    public string InspectorId=>Id; public string InspectorVersion=>Version;
    public bool Supports(string path)=>string.Equals(Path.GetExtension(path),".txt",StringComparison.OrdinalIgnoreCase);
    public async Task<SourceFactsDocument> InspectAsync(string sourcePath,CancellationToken cancellationToken=default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath); if(!File.Exists(sourcePath))throw new FileNotFoundException("Source TXT was not found.",sourcePath); if(!Supports(sourcePath))throw new NotSupportedException("TXT inspector supports .txt only.");
        var bytes=await File.ReadAllBytesAsync(sourcePath,cancellationToken); var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); var text=Decode(bytes); var facts=new List<SourceFact>();var order=0;var lines=text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        for(var i=0;i<lines.Length;i++){cancellationToken.ThrowIfCancellationRequested();if(lines[i].Length==0)continue;facts.Add(new SourceFact($"line:{i+1}",FactKind.TEXT_BLOCK,lines[i],order++,$"txt/line[{i+1}]",FactCertainty.SOURCE_EXACT,new Dictionary<string,string>{{"line",(i+1).ToString()}}));}
        return new(QualityCoreVersion.SourceFactsSchemaVersion,Id,Version,"txt",hash,facts);
    }
    private static string Decode(byte[] b){if(b.Length>=3&&b[0]==0xEF&&b[1]==0xBB&&b[2]==0xBF)return new UTF8Encoding(false,true).GetString(b,3,b.Length-3);if(b.Length>=2&&b[0]==0xFF&&b[1]==0xFE)return new UnicodeEncoding(false,true,true).GetString(b,2,b.Length-2);if(b.Length>=2&&b[0]==0xFE&&b[1]==0xFF)return new UnicodeEncoding(true,true,true).GetString(b,2,b.Length-2);return new UTF8Encoding(false,true).GetString(b);}
}
