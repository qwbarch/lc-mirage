module Mirage.Core.Field

open FSharpPlus

/// <summary>
/// A convenience type for class fields.
/// </summary>
type Field<'A> = Ref<Option<'A>>

/// <summary>
/// Initialize a field.
/// </summary>
let field<'A> () : Field<'A> = ref None

/// <summary>
/// A convenience type to make it simpler to create field getters.
/// </summary>
type Getter<'A> = Field<'A> -> string -> string -> Result<'A, string>

/// <summary>
/// Create a getter for an optional field, providing an error message if retrieving the value fails.
/// </summary>
let inline getter<'A> (className: string) (field: ref<Option<'A>>) (fieldName: string) (methodName: string) : Result<'A, string> =
    Option.toResultWith
        $"{className}#{methodName} was called while {fieldName} has not been initialized yet."
        field.Value

/// <summary>
/// Get the field's value.
/// </summary>
let inline getValue<'A> (field: Field<'A>) : Option<'A> = field.Value

/// <summary>
/// Set the value of a field.
/// </summary>
let inline set<'A> (field: Field<'A>) (value: 'A) =
    field.Value <- Some value

/// <summary>
/// Set the value of a field, whose type is nullable.
/// </summary>
let inline setNullable (field: Field<'A>) (value: 'A) =
    field.Value <- Option.ofObj value

/// <summary>
/// Set the value of a field.
/// </summary>
let inline setOption (field: Field<'A>) (value: Option<'A>) =
    field.Value <- value

/// <summary>
/// Set the field's value to <b>None</b>.
/// </summary>
let inline setNone (field: Field<'A>) =
    field.Value <- None