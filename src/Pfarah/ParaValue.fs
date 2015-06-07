namespace Pfarah

open Utils
open System
open System.IO
open System.Text
open System.Collections.Generic
open System.Globalization

[<RequireQualifiedAccess>]
type ParaValue =
  | Bool of bool
  | Number of float
  | Date of DateTime
  | String of string
  | Array of elements:ParaValue[]
  | Record of properties:(string * ParaValue)[]

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
    | "yes" -> ParaValue.Bool true
    | "no" -> ParaValue.Bool false
    | ParaNumber x -> ParaValue.Number x
    | ParaDate d -> ParaValue.Date d
    | x -> ParaValue.String x

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

    let result = new String(stringBuffer, 0, stringBufferCount)
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
      skipWhitespace stream
      vals.Add(parseValue())
      skipWhitespace stream

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
    skipWhitespace stream
    let pairs = ResizeArray<_>()
    pairs.Add((key, parseValue()))
    skipWhitespace stream
    while stopFn(stream) = false do
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
    skipWhitespace stream
    let key = readString()
    skipWhitespace stream
    assert (stream.Peek() = 61)
    stream.Read() |> ignore
    skipWhitespace stream
    let result = key, parseValue()
    skipWhitespace stream
    result

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

type private BinaryParaParser (stream:BinaryReader, lookup:IDictionary<int16, string>) =

  /// Looks up the human friendly name for an id. If the name does not exist,
  /// use the id's string value as a substitute. Don't error out because it is
  /// unreasonable for the client to know all ids that exist and will ever
  /// exist.
  let lookupId id =
      match lookup.TryGetValue(id) with
      | (false, _) -> id.ToString()
      | (true, x) -> x

  let rec parseTopObject () =
    let pairs = ResizeArray<_>()
    while stream.BaseStream.Position <> stream.BaseStream.Length do
      let key = lookupId (stream.ReadInt16())
      let equals = stream.ReadInt16()
      if equals <> 0x0001s then failwith "Expected equals token"
      pairs.Add((key, parseValue()))
    pairs.ToArray()

  and parseValue () =
    match stream.ReadInt16() with
    | 0x000cs ->
      let value = stream.ReadUInt32()
      let (left, hours) = Math.DivRem(int(value), 24)
      let (left, days) = Math.DivRem(left, 365)
      let years = left - 5001
      if years > 0 then
        let date =
          DateTime.MinValue
            .AddYears(years)
            .AddDays(float(days + 1))
            .AddHours(float(hours))
        ParaValue.Date(date)
      else ParaValue.Number(float(value))
    | 0x0014s -> ParaValue.Number(stream.ReadInt32() |> float)
    | 0x000es -> ParaValue.Bool(stream.ReadByte() = 0x00uy)
    | 0x000fs | 0x0017s ->
      let length = stream.ReadUInt16() |> int
      ParaValue.String(String(stream.ReadChars(length)))
    | 0x000ds -> ParaValue.Number(stream.ReadSingle() |> float)
    | 0x0003s -> ParaValue.Record(parseTopObject())
    | x -> failwith "Unrecognized type"

  member x.Parse (header:option<string>) =
    match header with
    | Some(txt) ->
      let (headerBuffer:char[]) = Array.zeroCreate txt.Length
      stream.Read(headerBuffer, 0, headerBuffer.Length) |> ignore
      if new String(headerBuffer) <> txt then
        failwith "Expected header not encountered"
      ParaValue.Record(parseTopObject())
    | None -> ParaValue.Record(parseTopObject())

type ParaValue with
  /// Parses the given stream
  static member Load (stream:Stream) =
    use stream = new StreamReader(stream, Encoding.GetEncoding(1252), false, 0x8000)
    let parser = ParaParser stream
    parser.Parse ()

  /// Parses the given stream
  static member LoadBinary (stream:Stream, lookup:IDictionary<int16, string>, header:option<string>) =
    use stream = new BinaryReader(stream, Encoding.GetEncoding(1252))
    let parser = BinaryParaParser(stream, lookup)
    parser.Parse (header)

  /// Parses the given file path, allowing other processes to read and write at the same time
  static member Load file =
    use fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 0x8000)
    ParaValue.Load fs

  /// Parses the given string
  static member Parse (text:string) =
    let str = new MemoryStream(Encoding.GetEncoding(1252).GetBytes(text))
    ParaValue.Load str

  /// Writes the given data to a stream
  static member Save (stream:Stream, data:ParaValue) =
    use stream = new StreamWriter(stream, Encoding.GetEncoding(1252), 0x8000)

    let rec recWrite props =
      props |> Array.iter (fun ((prop:string), v) ->
        stream.Write(prop)
        stream.Write('=')
        write v)
    and write = function
      | ParaValue.Bool b -> stream.WriteLine(if b = true then "yes" else "no")
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
