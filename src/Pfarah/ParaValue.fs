namespace Pfarah

open Utils
open System
open System.IO
open System.Text
open System.Collections.Generic
open Ionic.Zip
open System.Globalization


[<RequireQualifiedAccess>]
type ParaValue =
  | Bool of bool
  | Number of float
  | Date of DateTime
  | String of string
  | Array of elements:ParaValue[]
  | Record of properties:(string * ParaValue)[]
with
  /// Returns a string of the prettified structure. For simple cases it will
  /// just print the contents (string, bool, number, and date). For arrays and
  /// records, the result will contain a newline string of each value with
  /// each value indented depending on how nested the structure. The one exception
  /// to is when the array or object is empty, which will just print the brackets
  /// on the same line.
  static member private Prettify value indent =
    let nestedPrint inner = ParaValue.Prettify inner (indent + 1)
    let indentStr = System.String(' ', (indent + 1) * 2)
    match value with
    | String(x) -> x
    | Bool(x) -> sprintf "%b" x
    | Number(x) -> sprintf "%.3f" x
    | Date(x) -> (x.ToString("yyyy.M.d"))
    | Array(arr) ->
      let stringed = arr |> Array.map (nestedPrint >> (sprintf "%s%s" indentStr))
      let contents = String.Join(",\n", stringed)

      match contents with
      | "" -> "[]"
      | x -> sprintf "[\n%s\n%s]" x (System.String(' ', indent * 2))
    | Record(cord) ->
      let stringed = cord |> Seq.map (fun (key, v) -> sprintf "%s%s: %s" indentStr key (nestedPrint v))
      let contents = String.Join(",\n", stringed)

      match contents with
      | "" -> "{}"
      | x -> sprintf "{\n%s\n%s}" x (System.String(' ', indent * 2))

  override this.ToString() = ParaValue.Prettify this 0

module Frequencies =
  // Approximately 50% of numbers in savegames are either 0's or 1's. Other
  // popular numbers are -10 for opinion cache, 100000 for border distance,
  // and 2048 for flags
  let p0 = ParaValue.Number 0.0
  let p1 = ParaValue.Number 1.0

  /// Top keys found in a savegame. Roughly 25% of all keys
  let pflags = ParaValue.String "flags"
  let ptype = ParaValue.String "type"
  let pname = ParaValue.String "name"
  let pempty = ParaValue.String ""
  let pid = ParaValue.String "id"

  // There is only two possible values for booleans, so cache these bad boys
  let ptrue = ParaValue.Bool true
  let pfalse = ParaValue.Bool false

type private ParaParser (stream:StreamReader) =
  /// The max token size of any string, as defined by paradox internal source
  /// code is 256
  let (stringBuffer:char[]) = Array.zeroCreate 256

  /// Mutable variable to let us know how much of the string buffer is filled
  let mutable stringBufferCount = 0

  /// Returns whether a given int is considered whitespace
  let isspace (c:int) = c = 10 || c = 13 || c = 9 || c = 32

  /// Advance the stream until a non-whitespace character is encountered
  let skipWhitespace (stream:StreamReader) =
    while (isspace (stream.Peek())) do
      stream.Read() |> ignore

  /// Narrows a given string to a better data representation. If no better
  /// representation can be found then the string is returned.
  let narrow str =
    match str with
    | "yes" -> Frequencies.ptrue
    | "no" -> Frequencies.pfalse
    | ParaNumber x ->
      match x with
      | 0.0 -> Frequencies.p0
      | 1.0 -> Frequencies.p1
      | _ -> ParaValue.Number x
    | ParaDate d -> ParaValue.Date d
    | "flags" -> Frequencies.pflags
    | "type" -> Frequencies.ptype
    | "id" -> Frequencies.pid
    | "" -> Frequencies.pempty
    | "name" -> Frequencies.pname
    | x -> ParaValue.String x

  /// Trim leading and trailing whitespace from a value
  let trim fn =
    skipWhitespace(stream)
    let result = fn()
    skipWhitespace(stream)
    result

  let rec parseValue () =
    match stream.Peek() with
    | 34 -> parseQuotes()
    | 123 ->
      // Read through '{'
      stream.Read() |> ignore
      let result = parseContainer()

      // Read through '}'
      assert (stream.Peek() = 125)
      stream.Read() |> ignore
      result
    | _ -> narrow (readString())

  and readString () =
    let mutable isDone = false
    while not isDone do
      let next = stream.Peek()

      // We are done reading the current string if we hit whitespace an equal
      // sign, the end of a buffer, or a left curly (end of an object/list)
      isDone <- isspace next || next = 61 || next = -1 || next = 125
      if not (isDone) then
        stringBuffer.[stringBufferCount] <- (char (stream.Read()))
        stringBufferCount <- stringBufferCount + 1

    let result = getString stringBuffer stringBufferCount
    stringBufferCount <- 0
    result

  and quotedStringRead() =
    // Read until the next quote
    while stream.Peek() <> 34 do
      stringBuffer.[stringBufferCount] <- (char (stream.Read()))
      stringBufferCount <- stringBufferCount + 1

    // Read the trailing quote
    stream.Read() |> ignore
    let result = new String(stringBuffer, 0, stringBufferCount)
    stringBufferCount <- 0
    result

  and parseArray (vals:ResizeArray<_>) =
    while (stream.Peek() <> 125) do
      vals.Add(trim parseValue)

  and parseContainerContents() =
    // The first key or element depending on object or list
    let first = readString()
    skipWhitespace stream

    match (stream.Peek()) with
    | 125 -> // List of one
      let vals = ResizeArray<_>()
      vals.Add(narrow first)
      ParaValue.Array (vals.ToArray())

    // An equals sign means we are parsing an object
    | 61 -> parseObject first (fun (stream:StreamReader) -> stream.Peek() = 125)
    | _ -> // parse list
      skipWhitespace stream
      let vals = ResizeArray<_>()
      vals.Add(narrow first)
      parseArray vals
      ParaValue.Array (vals.ToArray())

  and parseContainer () =
    skipWhitespace stream
    match (stream.Peek()) with

    // Encountering a '}' means an empty object
    | 125 -> ParaValue.Record ([||])

    // Encountering a '{' means we are dealing with a nested list and a quote
    // means a quoted list
    | 123 | 34 ->
      let vals = ResizeArray<_>()
      parseArray vals
      ParaValue.Array (vals.ToArray())

    // Else we are not quite sure what we are parsing, so we need more info
    | _ -> parseContainerContents()

  and parseObject key stopFn =
    // Read through the '='
    stream.Read() |> ignore
    let pairs = ResizeArray<_>()
    pairs.Add(key, trim parseValue)
    while not(stopFn stream) do
      // Beware of empty objects "{}" that don't have a key. If we encounter
      // them, just blow right by them.
      if (stream.Peek()) = 123 then
        while (stream.Read()) <> 125 do
          ()
      else
        pairs.Add(parsePair())
      skipWhitespace stream
    ParaValue.Record (pairs.ToArray())

  and parseQuotes () =
    // Read through the quote
    stream.Read() |> ignore

    let q = quotedStringRead()
    match Utils.tryDateParse q with
    | Some(date) -> ParaValue.Date date
    | None -> ParaValue.String q

  and parsePair () =
    let key = trim readString
    assert (stream.Peek() = 61)
    stream.Read() |> ignore
    key, trim parseValue

  member x.Parse () =
    // Before we too far into parsing the stream we need to check if we have a
    // header. If we do see a header, ignore it.
    let pairs = ResizeArray<_>()
    skipWhitespace stream
    let first = readString()
    match (stream.Peek()) with
    | 10 | 13 ->
      while (not stream.EndOfStream) do
        pairs.Add(parsePair())
      ParaValue.Record (pairs.ToArray())
    | _ ->
      skipWhitespace stream
      assert (stream.Peek() = 61)
      parseObject first (fun stream -> stream.EndOfStream)

[<RequireQualifiedAccess>]
type private BinaryToken =
| String
| Uint
| Int
| Token
| Float
| Bool
| OpenGroup
| EndGroup
| Equals

type private BinaryParaParser (stream:BinaryReader, lookup:IDictionary<int16, string>) =

  /// The current token that the binary parser is looking at
  let mutable tok = BinaryToken.Token

  /// Performance optimization: Instead of making `BinaryToken` a discriminated
  /// union, inline all the options as mutable variables. `BinaryToken` is after
  /// all, private. This trick made binary parsing 25% faster with 25% less
  /// memory usage, at the cost of making the code less functional and harder to
  /// read.
  let mutable stringToken = ""
  let mutable uint32Token : uint32 = uint32 0
  let mutable int32Token : int32 = int32 0
  let mutable tokenString = ""
  let mutable floatToken = 0.0
  let mutable boolToken = false

  /// Reads a string from the stream, which is two octets of length followed
  /// by windows 1252 encoded characters.
  let readString () = String(stream.ReadChars(stream.ReadUInt16() |> int))

  /// throws an exception with stream byte position for easier debugging
  let fail preamble token =
    let str =
      match token with
      | BinaryToken.String -> sprintf "String: %s" stringToken
      | BinaryToken.Uint -> sprintf "Uint: %d" uint32Token
      | BinaryToken.Int -> sprintf "Int: %d" int32Token
      | BinaryToken.Token -> sprintf "Token: %s" tokenString
      | BinaryToken.Float -> sprintf "Float: %f" floatToken
      | BinaryToken.Bool -> sprintf "Bool: %b" boolToken
      | BinaryToken.OpenGroup -> "Open Group"
      | BinaryToken.EndGroup -> "End Group"
      | BinaryToken.Equals -> "Equals"
    failwithf "%s: %s, Position %d" preamble str stream.BaseStream.Position

  /// Looks up the human friendly name for an id. If the name does not exist,
  /// use the id's string value as a substitute. Don't error out because it is
  /// unreasonable for the client to know all ids that exist and will ever
  /// exist.
  let lookupId id =
      match lookup.TryGetValue(id) with
      | (false, _) -> id.ToString()
      | (true, x) -> x

  /// An integer may be a date. This active pattern attempts to detect such
  /// occurrences, though it doesn't guarantee 100% accuracy, as numbers larger
  /// than 43,808,760 will be detected as dates.
  let (|HiddenDate|_|) value =
    // Performance optimization: instead of checking after Math.DivRem that the
    // years fall between [0, 10000), this branch ensures that `value` will lead
    // to years between [0, 10000)
    if value >= 43808760 && value < 131408760 then
      let (left, hours) = Math.DivRem(int(value), 24)
      let (left, days) = Math.DivRem(left, 365)
      let years = left - 5001
      let date =
        DateTime.MinValue
          .AddYears(years)
          .AddDays(float(if years = 0 then 0 else days + 1))
          .AddHours(float(hours))
      Some(date)
    else None

  let parseDouble () =
    let res = stream.ReadInt32() |> cut

    // The next integer is seemingly unused
    stream.ReadInt32() |> ignore
    res

  let parseToken token =
    match token with
    | 0x0014s -> uint32Token <- stream.ReadUInt32(); BinaryToken.Uint
    | 0x000cs -> int32Token <- stream.ReadInt32(); BinaryToken.Int
    | 0x000es -> boolToken <- stream.ReadByte() <> 0uy; BinaryToken.Bool
    | 0x000fs | 0x0017s -> stringToken <- readString(); BinaryToken.String
    | 0x000ds -> floatToken <- stream.ReadInt32() |> cut32; BinaryToken.Float
    | 0x0167s -> floatToken <- parseDouble(); BinaryToken.Float
    | 0x0003s -> BinaryToken.OpenGroup
    | 0x0004s -> BinaryToken.EndGroup
    | 0x0001s -> BinaryToken.Equals
    | 0x284bs -> boolToken <- true; BinaryToken.Bool
    | 0x284cs -> boolToken <- false; BinaryToken.Bool
    | x -> tokenString <- lookupId x; BinaryToken.Token

  /// Returns whether a given token is an EndGroup
  let endGroup = function | BinaryToken.EndGroup -> true | _ -> false

  /// If the given token is not an Equals throw an exception
  let ensureEquals = function
    | BinaryToken.Equals -> ()
    | x -> fail "Expected equals, but got" x

  /// If the given token can't be used as an identifier, throw an exception
  let ensureIdentifier = function
    | BinaryToken.Int -> int32Token.ToString()
    | BinaryToken.Uint -> uint32Token.ToString()
    | BinaryToken.String -> stringToken
    | BinaryToken.Token -> tokenString
    | x -> fail "Expected identifier, but got" x

  /// Advances the stream to the next token and returns the token
  let nextToken () = tok <- parseToken (stream.ReadInt16()); tok

  /// Occasionally there are isolated "{}" without identifiers. I believe these
  /// to be useless, so this function will skip instances.
  let skipEmptyObjects () =
    while (match tok with | BinaryToken.OpenGroup -> true | _ -> false) do
      match (nextToken()) with
      | BinaryToken.EndGroup -> nextToken() |> ignore
      | x ->  fail "Expected empty object, but got" x

  let toPara = function
    | HiddenDate(date) -> ParaValue.Date(date)
    | num -> ParaValue.Number(float(num))

  let rec parseTopObject () =
    let pairs = ResizeArray<_>()
    while stream.BaseStream.Position <> stream.BaseStream.Length do
      let key = nextToken() |> ensureIdentifier
      nextToken() |> ensureEquals
      nextToken() |> ignore
      pairs.Add((key, parseValue()))
      skipEmptyObjects()
    pairs.ToArray()

  and parseObject firstKey =
    let pairs = ResizeArray<_>()

    // Advance reader to the next token
    nextToken() |> ignore
    pairs.Add((firstKey, parseValue()))
    nextToken() |> ignore
    skipEmptyObjects()

    while not (endGroup tok) do
      let key = tok |> ensureIdentifier
      nextToken() |> ensureEquals
      nextToken() |> ignore
      pairs.Add((key, parseValue()))
      nextToken() |> ignore
      skipEmptyObjects()
    pairs.ToArray()

  and parseArrayFirst first =
    let values = ResizeArray<_>()
    values.Add(first)
    parseArray(values)

  and parseArray (values:ResizeArray<_>) =
    while not (endGroup tok) do
      values.Add(parseValue())
      nextToken() |> ignore
    values.ToArray()

  /// Transforms current token into a ParaValue
  and parseValue () =
    match tok with
    | BinaryToken.Int -> toPara int32Token
    | BinaryToken.Uint -> ParaValue.Number(float(uint32Token))
    | BinaryToken.Bool -> ParaValue.Bool(boolToken)
    | BinaryToken.String -> ParaValue.String(stringToken)
    | BinaryToken.Float -> ParaValue.Number(floatToken)
    | BinaryToken.OpenGroup -> parseSubgroup()
    | BinaryToken.Token -> ParaValue.String(tokenString)
    | x -> fail "Unexpected token" x

  /// Determines what type of object follows an OpenGroup token -- an array or
  /// object.
  and parseSubgroup () =
    match (nextToken()) with
    | BinaryToken.Uint ->
      let temp = uint32Token
      subber (temp.ToString()) (fun () -> float temp |> ParaValue.Number)
    | BinaryToken.Int ->
      let temp = int32Token
      subber (temp.ToString()) (fun () -> toPara temp)
    | BinaryToken.Float ->
      let temp = floatToken
      nextToken() |> ignore
      ParaValue.Array(parseArrayFirst (ParaValue.Number(temp)))
    | BinaryToken.String ->
      let temp = stringToken
      subber temp (fun () -> ParaValue.String temp)
    | BinaryToken.OpenGroup ->
      let firstKey = nextToken() |> ensureIdentifier
      nextToken() |> ensureEquals
      let first = ParaValue.Record(parseObject firstKey)
      nextToken() |> ignore
      ParaValue.Array(parseArrayFirst first)
    | BinaryToken.Token ->
      nextToken() |> ensureEquals
      ParaValue.Record(parseObject tokenString)
    | BinaryToken.EndGroup -> ParaValue.Record [| |]
    | x -> fail "Unexpected token" x

  /// It's impossible to know just by reading the first token if we are dealing
  /// with an object or an array. The only way to know is to read the next
  /// token. Only if it is an equals are we dealing with an object.
  and subber (key: string) (paraFn:unit -> ParaValue) : ParaValue =
    match (nextToken()) with
    | BinaryToken.Equals -> ParaValue.Record(parseObject key)
    | BinaryToken.EndGroup -> ParaValue.Array([| paraFn() |])
    | _ ->
      let values = ResizeArray<_>()
      values.Add(paraFn())
      values.Add(parseValue())
      nextToken() |> ignore
      ParaValue.Array(parseArray values)

  member x.Parse (header:option<string>) =
    match header with
    | Some(txt) ->
      let (headerBuffer:char[]) = Array.zeroCreate txt.Length
      stream.Read(headerBuffer, 0, headerBuffer.Length) |> ignore
      if String(headerBuffer) <> txt then
        failwith "Expected header not encountered"
      ParaValue.Record(parseTopObject())
    | None -> ParaValue.Record(parseTopObject())

type ParaValue with
  /// Parses the given stream assuming that it contains strictly plain text.
  static member LoadText (stream:Stream) =
    use stream = new StreamReader(stream, Encoding.GetEncoding(1252), false, 0x8000)
    let parser = ParaParser stream
    parser.Parse ()

  /// Parses the given stream filled with binary data with a token lookup and
  /// header
  static member LoadBinary (stream:Stream, lookup:IDictionary<int16, string>, header:option<string>) =
    use stream = new BinaryReader(stream, Encoding.GetEncoding(1252))
    let parser = BinaryParaParser(stream, lookup)
    parser.Parse (header)

  /// Parses zip files or uncompressed files that can be plain text or binary
  /// encoded
  static member Load (file:string, binHeader:string, txtHeader:string, lookup:Lazy<IDictionary<int16, string>>) =
    use fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 0x8000)
    if (fs.ReadByte()) = 0x50 && (fs.ReadByte()) = 0x4b then
      use zf = new ZipFile(file)
      zf
      |> Seq.filter (fun x -> Path.GetExtension(x.FileName) <> "")
      |> Seq.exactlyOne
      |> fun x -> ParaValue.Load(x.OpenReader(), binHeader, txtHeader, lookup)
    else
      fs.Seek(0L, SeekOrigin.Begin) |> ignore
      ParaValue.Load(fs, binHeader, txtHeader, lookup)

  /// Given a stream that may be textually or binary encoded, the function
  /// parses it correctly when given the headers to look out for.
  static member Load (stream:Stream, binHeader:string, txtHeader:string, lookup:Lazy<IDictionary<int16, string>>) =
    if binHeader.Length <> txtHeader.Length then
      failwith "Headers should be the same length"
    let bt = Array.zeroCreate binHeader.Length
    let length = stream.Read(bt, 0, binHeader.Length)
    let header = Encoding.GetEncoding(1252).GetString(bt, 0, length)
    match header with
    | x when x = binHeader -> ParaValue.LoadBinary(stream, (lookup.Force()), None)
    | x when x = txtHeader -> ParaValue.LoadText(stream)
    | x -> failwithf "Unexpected header: %s" x

  /// Parses the given file path assuming that it contains strictly plain text.
  static member LoadText (file:string) =
    use fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 0x8000)
    ParaValue.LoadText fs

  /// Parses the given string
  static member Parse (text:string) =
    let str = new MemoryStream(Encoding.GetEncoding(1252).GetBytes(text))
    ParaValue.LoadText str

  /// Writes the given data to a stream
  static member Save (stream:Stream, data:ParaValue) =
    use stream = new StreamWriter(stream, Encoding.GetEncoding(1252), 0x8000)

    let rec recWrite props =
      props |> Array.iter (fun ((prop:string), v) ->
        stream.Write(prop)
        stream.Write('=')
        write v)
    and write = function
      | ParaValue.Bool b -> stream.WriteLine(if b then "yes" else "no")
      | ParaValue.Date d -> stream.WriteLine(d.ToString("yyyy.M.d"))
      | ParaValue.Number n -> stream.WriteLine(n.ToString("0.000"))
      | ParaValue.String s -> stream.WriteLine("\"" + s + "\"")
      | ParaValue.Array a ->
        stream.Write('{')
        Array.iter write a
        stream.Write('}')
      | ParaValue.Record r ->
        stream.Write('{')
        recWrite r
        stream.Write('}')

    match data with
    | ParaValue.Record props -> recWrite props
    | _ -> failwith "Can't save a non-record"
