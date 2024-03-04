(*
 * MIT License
 *
 * Copyright (c) 2023 Evaisa
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *)
module Mirage.Netcode

open FSharpPlus
open System.Reflection
open UnityEngine

let [<Literal>] private flags = BindingFlags.NonPublic ||| BindingFlags.Instance ||| BindingFlags.Static

let private invokeMethod (method: MethodInfo) =
    let attributes = method.GetCustomAttributes(typeof<RuntimeInitializeOnLoadMethodAttribute>, false)
    if attributes.Length > 0 then
        ignore <| method.Invoke(null, null)

/// <summary>
/// This must be run once (and only once) on plugin startup for the netcode patcher to work.<br />
/// See: https://github.com/EvaisaDev/UnityNetcodePatcher/tree/c64eb86e74e85e1badc442adc0bf270bab0df6b6#preparing-mods-for-patching
/// </summary>
let initNetcodePatcher () = Assembly.GetExecutingAssembly().GetTypes() >>= _.GetMethods(flags) |> iter invokeMethod