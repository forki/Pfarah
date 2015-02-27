/// Documentation for my library
///
/// ## Example
///
///     let h = Library.hello 1
///     printfn "%d" h
///
module Pfarah

open System
open System.IO
open System.Text

type PfData =
  | Pfbool of bool
  | Pfnumber of float
  | Pfdate of DateTime
  | Pfstring of string
  | Pflist of PfData list
  | PfObj of Map<string, PfData>


let isspace (c:int) = c = 10 || c = 13 || c = 9 || c = 32

let skipWhitespace (stream:StreamReader) =
  while (isspace (stream.Peek())) do
    stream.Read() |> ignore

let tryDate (str:string) =
  match str.Split('.') with
  | [|y;m;d|] -> Some(new DateTime(int y, int m, int d))
  | [|y;m;d;h|] -> Some(new DateTime(int y, int m, int d, int h, 0, 0))
  | _ -> None

let isnum (c:char) =
  c >= '0' && c <= '9'

type ParaParser (stream:StreamReader) =
  let MaxTokenSize = 256
  let (stringBuffer:char[]) = [||]
  let mutable stringBufferCount = 0
  let obj = PfObj Map.empty

  member self.readString () =
    let mutable isDone = false
    while isDone <> true do
      let next = stream.Peek()
      isDone <- isspace next || next = 61 || next = -1
      if not (isDone) then
        stringBuffer.[stringBufferCount] <- (char (stream.Read()))
        stringBufferCount <- stringBufferCount + 1

    let result = new String(stringBuffer, 0, stringBufferCount)
    stringBufferCount <- 0
    result

  member self.quotedStringRead() =
    while stream.Peek() <> 34 do
      stringBuffer.[stringBufferCount] <- (char (stream.Read()))
      stringBufferCount <- stringBufferCount + 1
    let result = new String(stringBuffer, 0, stringBufferCount)
    stringBufferCount <- 0
    result

  member self.Parse () =
    skipWhitespace stream
    let key = self.readString ()
    skipWhitespace stream

    // ASSERT (stream.Peek() = 61)
    stream.Read() |> ignore
    skipWhitespace stream
    let value =
      match stream.Peek() with
      | 34 ->
        let q = self.quotedStringRead()
        match tryDate q with
        | Some(date) -> Pfdate date
        | None -> Pfstring q
      | 123 -> PfObj Map.empty
      | _ -> Pfnumber 1.0

    obj

let parse file () =
  use fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 0x8000)
  use stream = new StreamReader(fs, Encoding.GetEncoding(1252), false, 0x8000)
  let parser = ParaParser stream
  parser.Parse ()
