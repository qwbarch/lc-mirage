module Assertion

open NUnit.Framework

let assertEquals<'A> (expected: 'A) (actual: 'A) = Assert.AreEqual(expected, actual)
let assertNotEquals<'A> (expected: 'A) (actual: 'A) = Assert.AreNotEqual(expected, actual)
let assertTrue condition errorMessage = Assert.IsTrue(condition, errorMessage)
let assertFail message = Assert.Fail(message)