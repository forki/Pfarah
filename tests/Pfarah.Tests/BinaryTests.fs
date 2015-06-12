﻿module Pfarah.BinaryTests

open Pfarah
open System
open System.IO
open NUnit.Framework

let shouldEqual (x : 'a) (y : 'a) = Assert.AreEqual(x, y, sprintf "Expected: %A\nActual: %A" x y)

let parse str lookup header = 
  match (ParaValue.LoadBinary(str, lookup, header)) with
  | ParaValue.Record properties -> properties
  | _ -> failwith "Expected a record"

let strm (arr:int[]) = new MemoryStream([| for i in arr -> byte(i) |])

[<Test>]
let ``binary parse basic date`` () =
  let lookup = dict([(0x284ds, "date")])
  let stream = strm([|0x4d; 0x28; 0x01; 0x00; 0x0c; 0x00; 0x10; 0x77; 0x5d; 0x03|])
  parse stream lookup None
  |> shouldEqual [| ("date", ParaValue.Date(DateTime(1444, 11, 11))) |]

[<Test>]
let ``binary parse basic string`` () =
  let lookup = dict([(0x2a38s, "player")])
  let stream = strm([|0x38; 0x2a; 0x01; 0x00; 0x0f; 0x00; 0x03; 0x00; 0x45; 0x4e; 0x47|])
  parse stream lookup None
  |> shouldEqual [| ("player", ParaValue.String "ENG") |]

[<Test>]
let ``binary top level multiple properties`` () =
  let lookup = dict([(0x284ds, "date"); (0x2a38s, "player")])
  let data =
    [| 0x4d; 0x28; 0x01; 0x00; 0x0c; 0x00; 0x10; 0x77; 0x5d; 0x03;
       0x38; 0x2a; 0x01; 0x00; 0x0f; 0x00; 0x03; 0x00; 0x45; 0x4e; 0x47|]
  parse (strm data) lookup None
  |> shouldEqual
    [| ("date", ParaValue.Date(DateTime(1444, 11, 11)))
       ("player", ParaValue.String "ENG") |]

[<Test>]
let ``binary parse basic date with header`` () =
  let lookup = dict([(0x284ds, "date")])
  let stream = strm([|0x45; 0x55; 0x34; 0x62; 0x69; 0x6e; 0x4d; 0x28;
                      0x01; 0x00; 0x0c; 0x00; 0x10; 0x77; 0x5d; 0x03|])
  parse stream lookup (Some("EU4bin"))
  |> shouldEqual [| ("date", ParaValue.Date(DateTime(1444, 11, 11))) |]

[<Test>]
let ``binary parse header failure`` () =
  let lookup = dict([(0x284ds, "date")])
  let stream = strm([|0x44; 0x55; 0x34; 0x62; 0x69; 0x6e; 0x4d; 0x28;
                      0x01; 0x00; 0x0c; 0x00; 0x10; 0x77; 0x5d; 0x03|])
  Assert.Throws((fun () ->
    ParaValue.LoadBinary(stream, lookup, (Some("EU4bin"))) |> ignore),
    "Expected header not encountered") |> ignore

[<Test>]
let ``binary parse nested object`` () =
  let lookup = dict([(0x2ec9s, "savegame_version")
                     (0x28e2s, "first")
                     (0x28e3s, "second")
                     (0x2ec7s, "third")
                     (0x2ec8s, "fourth")])
  let stream = strm([|0xc9; 0x2e; 0x01; 0x00; 0x03; 0x00; 0xe2; 0x28; 0x01; 0x00; 0x0c;
                    0x00; 0x01; 0x00; 0x00; 0x00; 0xe3; 0x28; 0x01; 0x00; 0x0c; 0x00;
                    0x0b; 0x00; 0x00; 0x00; 0xc7; 0x2e; 0x01; 0x00; 0x0c; 0x00; 0x04;
                    0x00; 0x00; 0x00; 0xc8; 0x2e; 0x01; 0x00; 0x0c; 0x00; 0x00; 0x00;
                    0x00; 0x00; 0x04; 0x00; |])
  parse stream lookup None
  |> shouldEqual
    [|("savegame_version",
       ParaValue.Record(
        [| ("first", ParaValue.Number 1.0)
           ("second", ParaValue.Number 11.0)
           ("third", ParaValue.Number 4.0)
           ("fourth", ParaValue.Number 0.0)|]) )|]

[<Test>]
let ``binary parse string array`` () =
  let lookup = dict([(0x2ee1s, "dlc_enabled")])
  let data =
    [| 0xe1; 0x2e; 0x01; 0x00; 0x03; 0x00; 0x0f; 0x00; 0x0a; 0x00; 0x41; 0x72; 0x74;
      0x20; 0x6f; 0x66; 0x20; 0x57; 0x61; 0x72; 0x0f; 0x00; 0x14; 0x00; 0x43; 0x6f;
      0x6e; 0x71; 0x75; 0x65; 0x73; 0x74; 0x20; 0x6f; 0x66; 0x20; 0x50; 0x61; 0x72;
      0x61; 0x64; 0x69; 0x73; 0x65; 0x0f; 0x00; 0x0b; 0x00; 0x52; 0x65; 0x73; 0x20;
      0x50; 0x75; 0x62; 0x6c; 0x69; 0x63; 0x61; 0x0f; 0x00; 0x11; 0x00; 0x57; 0x65;
      0x61; 0x6c; 0x74; 0x68; 0x20; 0x6f; 0x66; 0x20; 0x4e; 0x61; 0x74; 0x69; 0x6f;
      0x6e; 0x73; 0x04; 0x00; |]
  parse (strm data) lookup None
  |> shouldEqual
    [|("dlc_enabled", 
        ParaValue.Array([| ParaValue.String "Art of War"
                           ParaValue.String "Conquest of Paradise"
                           ParaValue.String "Res Publica"
                           ParaValue.String "Wealth of Nations" |]))|]

[<Test>]
let ``binary parse single string array`` () =
  let lookup = dict([(0x2ee1s, "dlc_enabled")])
  let data =
    [| 0xe1; 0x2e; 0x01; 0x00; 0x03; 0x00; 0x0f; 0x00; 0x0a; 0x00; 0x41; 0x72; 0x74;
      0x20; 0x6f; 0x66; 0x20; 0x57; 0x61; 0x72; 0x04; 0x00; |]
  parse (strm data) lookup None
  |> shouldEqual
    [|("dlc_enabled", 
        ParaValue.Array([| ParaValue.String "Art of War" |]))|]

[<Test>]
let ``binary parse int array`` () =
  let lookup = dict([(0x2c99s, "setgameplayoptions")])
  let data =
    [| 0x99; 0x2c; 0x01; 0x00; 0x03; 0x00; 0x0c; 0x00; 0x01; 0x00; 0x00; 0x00; 0x0c;
      0x00; 0x01; 0x00; 0x00; 0x00; 0x0c; 0x00; 0x00; 0x00; 0x00; 0x00; 0x0c; 0x00;
      0x00; 0x00; 0x00; 0x00; 0x0c; 0x00; 0x00; 0x00; 0x00; 0x00; 0x0c; 0x00; 0x01;
      0x00; 0x00; 0x00; 0x0c; 0x00; 0x00; 0x00; 0x00; 0x00; 0x0c; 0x00; 0x01; 0x00;
      0x00; 0x00; 0x0c; 0x00; 0x00; 0x00; 0x00; 0x00; 0x0c; 0x00; 0x01; 0x00; 0x00;
      0x00; 0x0c; 0x00; 0x00; 0x00; 0x00; 0x00; 0x0c; 0x00; 0x00; 0x00; 0x00; 0x00;
      0x0c; 0x00; 0x02; 0x00; 0x00; 0x00; 0x0c; 0x00; 0x00; 0x00; 0x00; 0x00; 0x0c;
      0x00; 0x00; 0x00; 0x00; 0x00; 0x0c; 0x00; 0x00; 0x00; 0x00; 0x00; 0x0c; 0x00;
      0x00; 0x00; 0x00; 0x00; 0x04; 0x00; |]
  parse (strm data) lookup None
  |> shouldEqual
    [|("setgameplayoptions",
       ParaValue.Array([|  ParaValue.Number 1.0
                           ParaValue.Number 1.0
                           ParaValue.Number 0.0
                           ParaValue.Number 0.0
                           ParaValue.Number 0.0
                           ParaValue.Number 1.0
                           ParaValue.Number 0.0
                           ParaValue.Number 1.0
                           ParaValue.Number 0.0
                           ParaValue.Number 1.0
                           ParaValue.Number 0.0
                           ParaValue.Number 0.0
                           ParaValue.Number 2.0
                           ParaValue.Number 0.0
                           ParaValue.Number 0.0
                           ParaValue.Number 0.0
                           ParaValue.Number 0.0 |])) |]

[<Test>]
let ``binary parse date array`` () =
  let lookup = dict([(0xdddds, "blah")])
  let data =
    [| 0xdd; 0xdd; 0x01; 0x00; 0x03; 0x00; 0x0c; 0x00; 0x10; 0x77; 0x5d; 0x03;
       0x04; 0x00|]
  parse (strm data) lookup None
  |> shouldEqual [| ("blah", ParaValue.Array([| ParaValue.Date(DateTime(1444, 11, 11)) |])) |]

[<Test>]
let ``binary parse float`` () =
  let data =
      [| 0x0f; 0x00; 0x03; 0x00; 0x45; 0x4e; 0x47; 0x01; 0x00; 0x0d; 0x00; 0x91;
         0xed; 0x87; 0x41 |]
  let stream = strm(data)
  let actual = ParaValue.LoadBinary(stream, dict([]), None)
  Assert.AreEqual(actual?ENG |> asFloat,  16.991, 0.01)

[<Test>]
let ``binary parse float array`` () =
  let data =
      [| 0x0f; 0x00; 0x03; 0x00; 0x45; 0x4e; 0x47; 0x01; 0x00; 0x03; 0x00;
         0x0d; 0x00; 0xb6; 0xb3; 0x9c; 0x42; 0x04; 0x00 |]
  let actual = ParaValue.LoadBinary(strm(data), dict([]), None)
  let arr = actual?ENG |> asArray
  let value = arr.[0] |> asFloat
  Assert.AreEqual(value, 78.351, 0.01)

[<Test>]
let ``binary parse deceptive object`` () =
  let lookup = dict([(0xdddds, "blah")])
  let data =
      [| 0xdd; 0xdd; 0x01; 0x00; 0x03; 0x00; 0x0f; 0x00; 0x03; 0x00; 0x45; 0x4e; 0x47;
         0x01; 0x00; 0x0c; 0x00; 0x01; 0x00; 0x00; 0x00; 0x04; 0x00; |]
  parse (strm data) lookup None
  |> shouldEqual [| ("blah", ParaValue.Record([| ("ENG", ParaValue.Number 1.0) |])) |]

[<Test>]
let ``binary parse signed int`` () =
  let lookup = dict([(0x2dc6s, "multiplayer_random_seed")])
  let data =
      [| 0xc6; 0x2d; 0x01; 0x00; 0x14; 0x00; 0x96; 0x00; 0x00; 0x00; |]
  parse (strm data) lookup None
  |> shouldEqual [| ("multiplayer_random_seed", ParaValue.Number 150.0) |]

[<Test>]
let ``binary parse empty array`` () =
  let lookup = dict([(0x2dc6s, "multiplayer_random_seed")])
  let data =
      [| 0xc6; 0x2d; 0x01; 0x00; 0x03; 0x00; 0x04; 0x00 |]
  parse (strm data) lookup None
  |> shouldEqual [| ("multiplayer_random_seed", ParaValue.Array [| |]) |]

[<Test>]
let ``binary parse object array`` () =
  let lookup = dict([(0x3088s, "army_templates")
                     (0x001bs, "name")
                     (0x3086s, "spread")
                     (0x2a0bs, "mercenary")
                     (0x2781s, "infantry")
                     (0x2780s, "cavalry")
                     (0x2782s, "artillery")])
  let data =
    [|0x88; 0x30; 0x01; 0x00; 0x03; 0x00; 0x03; 0x00; 0x1b; 0x00; 0x01; 0x00; 0x0f;
      0x00; 0x00; 0x00; 0x86; 0x30; 0x01; 0x00; 0x0c; 0x00; 0x01; 0x00; 0x00; 0x00;
      0x0b; 0x2a; 0x01; 0x00; 0x0e; 0x00; 0x01; 0x81; 0x27; 0x01; 0x00; 0x0c; 0x00;
      0x00; 0x00; 0x00; 0x00; 0x80; 0x27; 0x01; 0x00; 0x0c; 0x00; 0x00; 0x00; 0x00;
      0x00; 0x82; 0x27; 0x01; 0x00; 0x0c; 0x00; 0x00; 0x00; 0x00; 0x00; 0x04; 0x00;
      0x04; 0x00|]
  parse (strm data) lookup None
  |> shouldEqual
    [| ("army_templates",
        ParaValue.Array(
          [| ParaValue.Record(
              [| ("name", ParaValue.String "")
                 ("spread", ParaValue.Number 1.0)
                 ("mercenary", ParaValue.Bool true)
                 ("infantry", ParaValue.Number 0.0)
                 ("cavalry", ParaValue.Number 0.0)
                 ("artillery", ParaValue.Number 0.0) |]) |])) |]
