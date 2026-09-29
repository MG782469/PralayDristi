using System.Buffers.Binary;
using System.Text;

namespace PralayDrishti.Core.External;

/// <summary>
/// Just enough of the netCDF classic (CDF-1/CDF-2) format to write a D-Flow FM UGRID net file and read back a
/// map file written with NcFormat = 3. Big-endian, no compression, no groups - that's the whole format.
/// Pulling in a native netCDF library for this would make the air-gapped Docker image a lot heavier.
/// </summary>
public static class NetCdf3
{
    public enum NcType { Byte = 1, Char = 2, Short = 3, Int = 4, Float = 5, Double = 6 }

    public sealed class Dim
    {
        public required string Name { get; init; }
        public int Length { get; init; }                 // 0 = record (unlimited) dimension
    }

    public sealed class Attr
    {
        public required string Name { get; init; }
        public required object Value { get; init; }      // string, int, int[], double, double[]
    }

    public sealed class Var
    {
        public required string Name { get; init; }
        public required string[] Dims { get; init; }
        public required NcType Type { get; init; }
        public List<Attr> Attrs { get; init; } = new();
        public Array? Data { get; set; }                 // int[], double[], float[], byte[] (char)
        internal long Begin;
        internal long VSize;
    }

    public sealed class File
    {
        public List<Dim> Dims { get; } = new();
        public List<Attr> GlobalAttrs { get; } = new();
        public List<Var> Vars { get; } = new();
        public int NumRecords { get; set; }

        public Dim AddDim(string name, int len) { var d = new Dim { Name = name, Length = len }; Dims.Add(d); return d; }

        public Var AddVar(string name, NcType type, string[] dims, Array data, params (string, object)[] attrs)
        {
            var v = new Var { Name = name, Type = type, Dims = dims, Data = data };
            foreach (var (k, val) in attrs) v.Attrs.Add(new Attr { Name = k, Value = val });
            Vars.Add(v);
            return v;
        }

        public Var? Find(string name) => Vars.FirstOrDefault(v => v.Name == name);
    }

    // ---------- writer (fixed-size variables only, CDF-2 64-bit offsets) ----------

    public static void Write(File f, string path)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(fs);

        // first pass: sizes
        foreach (var v in f.Vars)
        {
            long n = 1;
            foreach (var d in v.Dims) n *= f.Dims.First(x => x.Name == d).Length;
            v.VSize = Pad4(n * TypeSize(v.Type));
        }
        long headerSize = MeasureHeader(f);
        long offset = headerSize;
        foreach (var v in f.Vars) { v.Begin = offset; offset += v.VSize; }

        WriteHeader(w, f);
        foreach (var v in f.Vars)
        {
            fs.Position = v.Begin;
            WriteData(w, v);
        }
    }

    private static long MeasureHeader(File f)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteHeader(w, f);
        return ms.Length;
    }

    private static void WriteHeader(BinaryWriter w, File f)
    {
        w.Write(Encoding.ASCII.GetBytes("CDF"));
        w.Write((byte)2);
        BeInt(w, f.NumRecords);
        if (f.Dims.Count == 0) { BeInt(w, 0); BeInt(w, 0); }
        else
        {
            BeInt(w, 0x0A);
            BeInt(w, f.Dims.Count);
            foreach (var d in f.Dims) { Name(w, d.Name); BeInt(w, d.Length); }
        }
        Attrs(w, f.GlobalAttrs);
        if (f.Vars.Count == 0) { BeInt(w, 0); BeInt(w, 0); }
        else
        {
            BeInt(w, 0x0B);
            BeInt(w, f.Vars.Count);
            foreach (var v in f.Vars)
            {
                Name(w, v.Name);
                BeInt(w, v.Dims.Length);
                foreach (var d in v.Dims) BeInt(w, f.Dims.FindIndex(x => x.Name == d));
                Attrs(w, v.Attrs);
                BeInt(w, (int)v.Type);
                BeInt(w, (int)Math.Min(v.VSize, int.MaxValue));
                BeLong(w, v.Begin);
            }
        }
    }

    private static void Attrs(BinaryWriter w, List<Attr> attrs)
    {
        if (attrs.Count == 0) { BeInt(w, 0); BeInt(w, 0); return; }
        BeInt(w, 0x0C);
        BeInt(w, attrs.Count);
        foreach (var a in attrs)
        {
            Name(w, a.Name);
            switch (a.Value)
            {
                case string s:
                    var b = Encoding.UTF8.GetBytes(s);
                    BeInt(w, (int)NcType.Char); BeInt(w, b.Length); w.Write(b); PadTo4(w, b.Length);
                    break;
                case int i:
                    BeInt(w, (int)NcType.Int); BeInt(w, 1); BeInt(w, i);
                    break;
                case int[] ia:
                    BeInt(w, (int)NcType.Int); BeInt(w, ia.Length); foreach (var x in ia) BeInt(w, x);
                    break;
                case double d:
                    BeInt(w, (int)NcType.Double); BeInt(w, 1); BeDouble(w, d);
                    break;
                case double[] da:
                    BeInt(w, (int)NcType.Double); BeInt(w, da.Length); foreach (var x in da) BeDouble(w, x);
                    break;
                default:
                    throw new NotSupportedException($"attribute type {a.Value.GetType()}");
            }
        }
    }

    private static void WriteData(BinaryWriter w, Var v)
    {
        long written = 0;
        switch (v.Data)
        {
            case int[] ia: foreach (var x in ia) BeInt(w, x); written = ia.Length * 4L; break;
            case double[] da: foreach (var x in da) BeDouble(w, x); written = da.Length * 8L; break;
            case float[] fa:
                foreach (var x in fa)
                {
                    Span<byte> b = stackalloc byte[4];
                    BinaryPrimitives.WriteSingleBigEndian(b, x);
                    w.Write(b);
                }
                written = fa.Length * 4L;
                break;
            case byte[] ba: w.Write(ba); written = ba.Length; break;
        }
        PadTo4(w, written);
    }

    private static void Name(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        BeInt(w, b.Length);
        w.Write(b);
        PadTo4(w, b.Length);
    }

    private static void PadTo4(BinaryWriter w, long n)
    {
        long pad = Pad4(n) - n;
        for (int i = 0; i < pad; i++) w.Write((byte)0);
    }

    private static long Pad4(long n) => (n + 3) & ~3L;

    private static void BeInt(BinaryWriter w, int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); w.Write(b); }
    private static void BeLong(BinaryWriter w, long v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(b, v); w.Write(b); }
    private static void BeDouble(BinaryWriter w, double v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteDoubleBigEndian(b, v); w.Write(b); }

    private static int TypeSize(NcType t) => t switch
    {
        NcType.Byte or NcType.Char => 1,
        NcType.Short => 2,
        NcType.Int or NcType.Float => 4,
        _ => 8
    };

    // ---------- reader (header + any variable, record vars included) ----------

    public sealed class Reader : IDisposable
    {
        private readonly FileStream _fs;
        private readonly BinaryReader _r;
        private readonly int _version;
        public File Header { get; } = new();
        private long _recSize;

        public Reader(string path)
        {
            _fs = System.IO.File.OpenRead(path);
            _r = new BinaryReader(_fs);
            var magic = _r.ReadBytes(3);
            if (Encoding.ASCII.GetString(magic) != "CDF")
                throw new InvalidDataException("not a netCDF classic file (netCDF-4/HDF5 is not supported - set NcFormat = 3 in the .mdu)");
            _version = _r.ReadByte();
            Header.NumRecords = ReadInt();
            ReadDims();
            Header.GlobalAttrs.AddRange(ReadAttrs());
            ReadVars();
            _recSize = Header.Vars.Where(IsRecord).Sum(v => v.VSize);
            // special case from the spec: a single record variable is not padded
            if (Header.Vars.Count(IsRecord) == 1)
            {
                var v = Header.Vars.First(IsRecord);
                _recSize = RecordElements(v) * TypeSize(v.Type);
            }
        }

        private bool IsRecord(Var v) => v.Dims.Length > 0 && Header.Dims.First(d => d.Name == v.Dims[0]).Length == 0;

        private long RecordElements(Var v)
        {
            long n = 1;
            foreach (var d in v.Dims.Skip(1)) n *= Header.Dims.First(x => x.Name == d).Length;
            return n;
        }

        public int DimLength(string name)
        {
            var d = Header.Dims.FirstOrDefault(x => x.Name == name) ?? throw new KeyNotFoundException(name);
            return d.Length == 0 ? Header.NumRecords : d.Length;
        }

        /// <summary>Reads a variable as doubles. For record variables pass the record (time) index.</summary>
        public double[] ReadDoubles(string name, int record = 0)
        {
            var v = Header.Find(name) ?? throw new KeyNotFoundException($"variable {name} not in file");
            long count;
            long pos;
            if (IsRecord(v))
            {
                count = RecordElements(v);
                pos = v.Begin + record * _recSize;
            }
            else
            {
                count = 1;
                foreach (var d in v.Dims) count *= DimLength(d);
                pos = v.Begin;
            }
            _fs.Position = pos;
            var res = new double[count];
            int sz = TypeSize(v.Type);
            var buf = _r.ReadBytes((int)(count * sz));
            for (int i = 0; i < count; i++)
            {
                var s = buf.AsSpan(i * sz, sz);
                res[i] = v.Type switch
                {
                    NcType.Double => BinaryPrimitives.ReadDoubleBigEndian(s),
                    NcType.Float => BinaryPrimitives.ReadSingleBigEndian(s),
                    NcType.Int => BinaryPrimitives.ReadInt32BigEndian(s),
                    NcType.Short => BinaryPrimitives.ReadInt16BigEndian(s),
                    _ => s[0]
                };
            }
            return res;
        }

        private void ReadDims()
        {
            int tag = ReadInt(), n = ReadInt();
            if (tag == 0) return;
            for (int i = 0; i < n; i++) Header.Dims.Add(new Dim { Name = ReadName(), Length = ReadInt() });
        }

        private List<Attr> ReadAttrs()
        {
            var list = new List<Attr>();
            int tag = ReadInt(), n = ReadInt();
            if (tag == 0) return list;
            for (int i = 0; i < n; i++)
            {
                string name = ReadName();
                var type = (NcType)ReadInt();
                int len = ReadInt();
                int sz = TypeSize(type);
                var bytes = _r.ReadBytes(len * sz);
                _r.ReadBytes((int)(Pad4(len * sz) - len * sz));
                object val = type == NcType.Char ? Encoding.UTF8.GetString(bytes) : len;
                list.Add(new Attr { Name = name, Value = val });
            }
            return list;
        }

        private void ReadVars()
        {
            int tag = ReadInt(), n = ReadInt();
            if (tag == 0) return;
            for (int i = 0; i < n; i++)
            {
                string name = ReadName();
                int nd = ReadInt();
                var dims = new string[nd];
                for (int k = 0; k < nd; k++) dims[k] = Header.Dims[ReadInt()].Name;
                var attrs = ReadAttrs();
                var type = (NcType)ReadInt();
                long vsize = (uint)ReadInt();
                long begin = _version == 1 ? ReadInt() : ReadLong();
                Header.Vars.Add(new Var { Name = name, Dims = dims, Type = type, Attrs = attrs, VSize = vsize, Begin = begin });
            }
        }

        private string ReadName()
        {
            int len = ReadInt();
            var b = _r.ReadBytes(len);
            _r.ReadBytes((int)(Pad4(len) - len));
            return Encoding.UTF8.GetString(b);
        }

        private int ReadInt() => BinaryPrimitives.ReadInt32BigEndian(_r.ReadBytes(4));
        private long ReadLong() => BinaryPrimitives.ReadInt64BigEndian(_r.ReadBytes(8));

        public void Dispose()
        {
            _r.Dispose();
            _fs.Dispose();
        }
    }
}
