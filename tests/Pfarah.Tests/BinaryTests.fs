﻿module Pfarah.BinaryTests

open Pfarah
open System
open System.IO
open NUnit.Framework
open Utils

let shouldEqual (x : 'a) (y : 'a) = Assert.AreEqual(x, y, sprintf "Expected: %A\nActual: %A" x y)

let parse str lookup header =
  match (ParaValue.LoadBinary(str, lookup, header)) with
  | ParaValue.Record properties -> properties
  | _ -> failwith "Expected a record"

let strm (arr:int[]) = new MemoryStream(Array.map byte arr)

[<Test>]
let ``binary parse basic date`` () =
  let lookup = dict([(0x284ds, "date")])
  let stream = strm([|0x4d; 0x28; 0x01; 0x00; 0x0c; 0x00; 0x10; 0x77; 0x5d; 0x03|])
  parse stream lookup None
  |> shouldEqual [| ("date", ParaValue.Date(DateTime(1444, 11, 11))) |]

[<Test>]
let ``binary parse 1.1.1 date`` () =
  let lookup = dict([(0x284ds, "date")])
  let stream = strm([|0x4d; 0x28; 0x01; 0x00; 0x0c; 0x00; 0xf8; 0x77; 0x9c; 0x02|])
  parse stream lookup None
  |> shouldEqual [| ("date", ParaValue.Date(DateTime(1, 1, 1))) |]

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
let ``load binary data`` () =
  let lookup = dict([(0x284ds, "date")])
  let stream = strm([|0x45; 0x55; 0x34; 0x62; 0x69; 0x6e; 0x4d; 0x28;
                      0x01; 0x00; 0x0c; 0x00; 0x10; 0x77; 0x5d; 0x03|])
  ParaValue.Load(stream, "EU4bin", "EU4txt", lazy lookup)
  |> shouldEqual (ParaValue.Record([| ("date", ParaValue.Date(DateTime(1444, 11, 11))) |]))

[<Test>]
let ``binary parse header failure`` () =
  let lookup = dict([(0x284ds, "date")])
  let stream = strm([|0x44; 0x55; 0x34; 0x62; 0x69; 0x6e; 0x4d; 0x28;
                      0x01; 0x00; 0x0c; 0x00; 0x10; 0x77; 0x5d; 0x03|])
  let ex = Assert.Throws(fun () ->
    ParaValue.LoadBinary(stream, lookup, (Some("EU4bin"))) |> ignore)
  ex.Message |> shouldEqual "Expected header not encountered"

[<Test>]
let ``binary parse object without equals fail`` () =
  let data = [| 0xdd; 0xdd; 0x03; 0x00 |]
  let ex = Assert.Throws(fun () -> ParaValue.LoadBinary((strm data), dict([]), None) |> ignore)
  ex.Message |> shouldEqual "Expected equals, but got: Open Group, Position 4"

[<Test>]
let ``binary parse object invalid equals token`` () =
  let data = [| 0xdd; 0xdd; 0x01; 0x00; 0x01; 0x00 |]
  let ex = Assert.Throws(fun () -> ParaValue.LoadBinary((strm data), dict([]), None) |> ignore)
  ex.Message |> shouldEqual "Unexpected token: Equals, Position 6"

[<Test>]
let ``binary parse subgroup unexpected equals token`` () =
  let data = [| 0xdd; 0xdd; 0x01; 0x00; 0x03; 0x00; 0x01; 0x00 |]
  let ex = Assert.Throws(fun () -> ParaValue.LoadBinary((strm data), dict([]), None) |> ignore)
  ex.Message |> shouldEqual "Unexpected token: Equals, Position 8"

[<Test>]
let ``binary parse must start with an identifier`` () =
  let data = [| 0x01; 0x00; |]
  let ex = Assert.Throws(fun () -> ParaValue.LoadBinary((strm data), dict([]), None) |> ignore)
  ex.Message |> shouldEqual "Expected identifier, but got: Equals, Position 2"

[<Test>]
let ``0x000cs means int32 data`` () =
  let lookup = dict([(0xdddds, "provinces")])
  let data = [| 0xdd; 0xdd; 0x01; 0x00; 0x0c; 0x00; 0xff; 0xff; 0xff; 0xff |]
  parse (strm data) lookup None
  |> shouldEqual [| ("provinces", ParaValue.Number -1.0 ) |]

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


let (``try parse float cases``:obj[][]) = [|
  [| [| 0x17; 0x00; 0x00; 0x00 |]; 0.023 |]
  [| [| 0x29; 0x00; 0x00; 0x00 |]; 0.041 |]
  [| [| 0x12; 0x00; 0x00; 0x00 |]; 0.018 |]
  [| [| 0x1e; 0x02; 0x00; 0x00 |]; 0.542 |]
  [| [| 0xe8; 0x03; 0x00; 0x00 |]; 1.000 |]
  [| [| 0xc0; 0xc6; 0x2d; 0x00 |]; 3000.000 |]
|]

[<Test>]
[<TestCaseSource("try parse float cases")>]
let ``binary parse float`` data (expected:float) =
  let header = [| 0x0f; 0x00; 0x03; 0x00; 0x45; 0x4e; 0x47; 0x01; 0x00; 0x0d; 0x00; |]
  let data = Array.concat([header; data])
  let stream = strm(data)
  let actual = ParaValue.LoadBinary(stream, dict([]), None)
  Assert.AreEqual(actual?ENG |> asFloat,  expected, 0.01)

[<Test>]
let ``binary parse float array`` () =
  let data =
      [| 0x0f; 0x00; 0x03; 0x00; 0x45; 0x4e; 0x47; 0x01; 0x00; 0x03; 0x00;
         0x0d; 0x00; 0x17; 0x00; 0x00; 0x00; 0x04; 0x00 |]
  let actual = ParaValue.LoadBinary(strm(data), dict([]), None)
  let arr = actual?ENG |> asArray
  let value = arr.[0] |> asFloat
  Assert.AreEqual(value, 0.023, 0.01)

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
let ``binary parse signed int array`` () =
  let lookup = dict([(0x2dc6s, "multiplayer_random_seed")])
  let data =
      [| 0xc6; 0x2d; 0x01; 0x00; 0x03; 0x00; 0x14; 0x00; 0x96; 0x00; 0x00;
         0x00; 0x14; 0x00; 0x96; 0x00; 0x00; 0x00; 0x04; 0x00; |]
  parse (strm data) lookup None
  |> shouldEqual
    [| ("multiplayer_random_seed", ParaValue.Array([| ParaValue.Number 150.0
                                                      ParaValue.Number 150.0 |])) |]

[<Test>]
let ``binary parse empty object`` () =
  let lookup = dict([(0xdddds, "foo")])
  let data =
      [| 0xdd; 0xdd; 0x01; 0x00; 0x03; 0x00; 0x04; 0x00 |]
  parse (strm data) lookup None
  |> shouldEqual [| ("foo", ParaValue.Record [| |]) |]

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
[<Test>]
let ``binary parse ignore empty objects`` () =
  let lookup = dict([(0xdddds, "foo"); (0x2a05s, "bar")])
  let data =
    [| 0xdd; 0xdd; 0x01; 0x00; 0x03; 0x00; 0x05; 0x2a; 0x01; 0x00; 0x0f; 0x00;
       0x03; 0x00; 0x53; 0x57; 0x45; 0x03; 0x00; 0x04; 0x00; 0x04; 0x00 |]
  parse (strm data) lookup None
  |> shouldEqual
    [| ("foo", ParaValue.Record([| ("bar", ParaValue.String "SWE") |])) |]

[<Test>]
let ``binary parse ignore empty objects multiple`` () =
  let lookup = dict([(0xdddds, "foo"); (0x2a05s, "bar")])
  let data =
    [| 0xdd; 0xdd; 0x01; 0x00; 0x03; 0x00; 0x05; 0x2a; 0x01; 0x00; 0x0f; 0x00;
       0x03; 0x00; 0x53; 0x57; 0x45; 0x03; 0x00; 0x04; 0x00; 0x03; 0x00; 0x04;
       0x00; 0x04; 0x00 |]
  parse (strm data) lookup None
  |> shouldEqual
    [| ("foo", ParaValue.Record([| ("bar", ParaValue.String "SWE") |])) |]

[<Test>]
let ``binary parse ignore empty objects failure`` () =
  let lookup = dict([(0xdddds, "foo"); (0x2a05s, "bar")])
  let data =
    [| 0xdd; 0xdd; 0x01; 0x00; 0x03; 0x00; 0x05; 0x2a; 0x01; 0x00; 0x0f; 0x00;
       0x03; 0x00; 0x53; 0x57; 0x45; 0x03; 0x00; 0x01; 0x00; 0x04; 0x00 |]

  let ex = Assert.Throws(fun () -> ParaValue.LoadBinary((strm data), lookup, None) |> ignore)
  ex.Message |> shouldEqual "Expected empty object, but got: Equals, Position 21"

[<Test>]
let ``date impersonator`` () =
  let lookup = dict([(0xdddds, "foo")]);
  let data = [| 0xdd; 0xdd; 0x01; 0x00; 0x0c; 0x00; 0x31; 0x9c; 0x45; 0x0f; |]
  parse (strm data) lookup None
  |> shouldEqual [| ("foo", ParaValue.Number 256220209.0) |]

[<Test>]
let ``parse no token`` () =
  let lookup = dict([(0x2d82s, "active")])
  let data = [| 0x82; 0x2d; 0x01; 0x00; 0x4c; 0x28 |]
  parse (strm data) lookup None
  |> shouldEqual [| ("active", ParaValue.Bool false) |]

[<Test>]
let ``parse yes token`` () =
  let lookup = dict([(0x2d82s, "active")])
  let data = [| 0x82; 0x2d; 0x01; 0x00; 0x4b; 0x28 |]
  parse (strm data) lookup None
  |> shouldEqual [| ("active", ParaValue.Bool true) |]

[<Test>]
let ``binary parse other token`` () =
  let lookup = dict([(0x00E1s, "type"); (0x28BEs, "general")])
  let data = [| 0xe1; 0x00; 0x01; 0x00; 0xbe; 0x28;  |]
  parse (strm data) lookup None
  |> shouldEqual [| ("type", ParaValue.String "general") |]

[<Test>]
let ``binary numerical identifier`` () =
  let lookup = dict([(0x2dc1s, "foo")])
  let data =
    [| 0xc1; 0x2d; 0x01; 0x00; 0x03; 0x00; 0x0c; 0x00; 0x59; 0x00; 0x00; 0x00; 0x01; 0x00;
       0x0c; 0x00; 0x1e; 0x00; 0x00; 0x00; 0x04; 0x00; |]
  parse (strm data) lookup None
  |> shouldEqual [| ("foo", ParaValue.Record([| ("89", ParaValue.Number 30.0) |])) |]

[<Test>]
let ``binary numerical identifier multiple`` () =
  let lookup = dict([(0x2dc1s, "foo")])
  let data =
    [| 0xc1; 0x2d; 0x01; 0x00; 0x03; 0x00; 0x0c; 0x00; 0x59; 0x00; 0x00; 0x00; 0x01; 0x00;
       0x0c; 0x00; 0x1e; 0x00; 0x00; 0x00; 0x0c; 0x00; 0x59; 0x00; 0x00; 0x00; 0x01; 0x00;
       0x0c; 0x00; 0x1e; 0x00; 0x00; 0x00; 0x04; 0x00; |]
  parse (strm data) lookup None
  |> shouldEqual [| ("foo", ParaValue.Record([| ("89", ParaValue.Number 30.0)
                                                ("89", ParaValue.Number 30.0) |])) |]

[<Test>]
let ``binary numerical identifier multiple int`` () =
  let lookup = dict([(0x2dc1s, "foo")])
  let data =
    [| 0xc1; 0x2d; 0x01; 0x00; 0x03; 0x00; 0x14; 0x00; 0xb5; 0x13; 0x00; 0x00; 0x01; 0x00;
       0x0c; 0x00; 0x1e; 0x00; 0x00; 0x00; 0x14; 0x00; 0xb5; 0x13; 0x00; 0x00; 0x01; 0x00;
       0x0c; 0x00; 0x1e; 0x00; 0x00; 0x00; 0x04; 0x00; |]
  parse (strm data) lookup None
  |> shouldEqual [| ("foo", ParaValue.Record([| ("5045", ParaValue.Number 30.0)
                                                ("5045", ParaValue.Number 30.0) |])) |]

[<Test>]
let ``binary parse heterogeneous array`` () =
  let lookup = dict([0xdddds, "foo"])
  let data =
    [| 0xdd; 0xdd; 0x01; 0x00; 0x03; 0x00; 0x0c; 0x00; 0x00; 0x00; 0x00; 0x00;
       0x0d; 0x00; 0x17; 0x00; 0x00; 0x00; 0x04; 0x00 |]
  let res = ParaValue.LoadBinary((strm data), lookup, None)
  let foo = res?foo |> asArray
  Assert.AreEqual(ParaValue.Number 0.0, foo.[0])
  Assert.AreEqual(0.023, foo.[1] |> asFloat, 0.001)


let (``try parse double cases``:obj[][]) = [|
  [| [| 0x00; 0x40; 0x08; 0x00 |]; 16.50000 |]
  [| [| 0x70; 0xca; 0x07; 0x00 |]; 15.58154 |]
  [| [| 0x00; 0x00; 0x00; 0x00 |]; 0.00000 |]
  [| [| 0xa5; 0xeb; 0x16; 0x00 |]; 45.84097 |]
  [| [| 0xc7; 0xe4; 0x00; 0x00 |]; 1.78732 |]
  [| [| 0xc2; 0xb5; 0x00; 0x00 |]; 1.41998 |]
|]

[<Test>]
[<TestCaseSource("try parse double cases")>]
let ``binary parse double data`` data expected =
  let bytes = data |> Array.map byte
  let value = BitConverter.ToInt32(bytes, 0)
  cut value |> shouldEqual expected

[<Test>]
let ``binary parse double`` () =
  let lookup = dict([0x2c2fs, "military_strength"])
  let data = [| 0x2f; 0x2c; 0x01; 0x00; 0x67; 0x01;
                0xc7; 0xe4; 0x00; 0x00; 0x00; 0x00; 0x00; 0x00 |]
  parse (strm data) lookup None
  |> shouldEqual [| ("military_strength", ParaValue.Number(1.78732)) |]

[<Test>]
let ``load plain binary file`` () =
  let path = Path.Combine("data", "eu4bin.eu4")
  ParaValue.Load(path, "EU4bin", "EU4txt", lazy(dict([(0x284ds, "date")])))
  |> shouldEqual (ParaValue.Record([| ("date", ParaValue.Date (DateTime(1757, 8, 12)))|]))

[<Test>]
let ``load zip binary file`` () =
  let path = Path.Combine("data", "eu4bin-zip.eu4")
  ParaValue.Load(path, "EU4bin", "EU4txt", lazy(dict([(0x284ds, "date")])))
  |> shouldEqual (ParaValue.Record([| ("date", ParaValue.Date (DateTime(1757, 8, 12)))|]))

[<Test>]
let ``load zip binary file big`` () =
  let path = Path.Combine("data", "eu4bin-zip-big.eu4")
  ParaValue.Load(path, "EU4bin", "EU4txt", lazy(dict([(0x284ds, "date")])))
  |> tryFind "date"
  |> Option.map asDate
  |> shouldEqual (Some (DateTime(1757, 8, 12)))
