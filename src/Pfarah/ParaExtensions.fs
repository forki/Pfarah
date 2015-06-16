﻿namespace Pfarah

[<AutoOpen>]
module ParaExtensions =

  /// Retrieves the property on the ParaValue Record with the given property
  /// name. If the object is not a Record or the property does not exist on
  /// the Record, the function will fail
  let (?) (obj:ParaValue) propertyName =
    match obj with
    | ParaValue.Record properties ->
      match Array.tryFind (fst >> (=) propertyName) properties with
      | Some (_, value) -> value
      | None -> failwithf "Didn't find property '%s' in %A" propertyName obj
    | _ -> failwithf "Not an object: %A" obj

  /// Get the integer value of an object
  let asInteger = function
    | ParaValue.Number n -> int n
    | x -> failwithf "Not an integer: %s" (x.ToString())

  /// Get the string value of an object
  let asString = function
    | ParaValue.String s -> s
    | x -> x.ToString()

  /// Get the floating point precision value of an object
  let asFloat = function
    | ParaValue.Number n -> n
    | x -> failwithf "Not a float: %s" (x.ToString())

  /// Get the boolean value of an object
  let asBool = function
    | ParaValue.Bool b -> b
    | ParaValue.Number n -> (int n) <> 0
    | x -> failwithf "Not a bool: %s" (x.ToString())

  /// Get the date value of an object
  let asDate = function
    | ParaValue.Date d -> d
    | x -> failwithf "Not a date: %s" (x.ToString())

  /// Returns the integer value if it exists else 0
  let integerDefault = function Some(x) -> x |> asInteger | None -> 0

  /// Returns the string value if it exists else the empty string
  let stringDefault = function Some(x) -> x |> asString | None -> ""

  /// Returns the float value if it exists else 0.0
  let floatDefault = function Some(x) -> x |> asFloat | None -> 0.0

  /// Returns the boolean value if it exists else false
  let boolDefault = function Some(x) -> x |> asBool | None -> false

  /// Get the array value of an object
  let asArray = function
    | ParaValue.Array elements -> elements
    | x -> failwithf "Not an array: %s" (x.ToString())

  /// Get the record value of the object
  let asRecord = function
    | ParaValue.Record properties -> properties
    | x -> failwithf "Not a record: %s" (x.ToString())

  /// Finds all the properties of the object with a given key and aggregates
  /// all the values under a single array.
  let collect prop obj =
    match obj with
    | ParaValue.Record properties ->
      properties
      |> Array.filter (fst >> (=) prop)
      |> Array.map snd
    | _ -> failwithf "Not an object: %A" obj

  /// Tries to find the first property of the object that has the given key.
  /// If a property is found then `Some ParaValue` will be returned else
  /// `None`
  let tryFind prop obj =
    match obj with
    | ParaValue.Record properties ->
      properties |> Array.tryFind (fst >> (=) prop) |> Option.map snd
    | _ -> None

  /// Given a sequence of similar objects, return sequence of tuples where it
  /// is the name of the property and a boolean value denoting whether the
  /// property did not occur in all given objects.
  ///
  /// ## Example Return Value
  ///
  ///     [("hello", true); ("world", false)]
  ///
  /// means that the "hello" property is optional and the "world" property is
  /// required in each object
  let findOptional (objs:seq<ParaValue>) =
    // Boil the given objects down to top level property names
    let props = objs |> Seq.map (asRecord >> (Seq.map fst) >> Set.ofSeq)

    let all = Set.unionMany props
    let optional = props |> Seq.map (Set.difference all) |> Set.unionMany

    let td isRequired = Seq.map (fun x -> (x, isRequired))
    Seq.append (Set.difference all optional |> td false) (optional |> td true)

  type ParaValue with
    /// Assumes the object is an array and returns the enumerator for the
    /// array
    member x.GetEnumerator () = asArray(x).GetEnumerator()

    /// Assumes the object is an array and returns the value at a given index
    /// in the array
    member x.Item(index) = asArray(x).[index]
