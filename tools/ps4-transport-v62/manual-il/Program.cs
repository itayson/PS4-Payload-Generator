using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
    throw new ArgumentException("Usage: PatchedManualTests original.dll patched.dll");

using var original = AssemblyDefinition.ReadAssembly(args[0]);
using var patched = AssemblyDefinition.ReadAssembly(args[1]);

var before = original.MainModule.GetType("PS4RemotePkgMobile.MainPage");
var after = patched.MainModule.GetType("PS4RemotePkgMobile.MainPage");
var legacy = before.Methods.Single(m => m.Name == "BtnInjetar_Clicked");
var guarded = after.Methods.Single(m => m.Name == "BtnInjetar_Clicked");
var instructions = guarded.Body.Instructions;

if (legacy.Body.Instructions.Count + 4 != instructions.Count)
    throw new Exception("Manual-send body was rewritten instead of prefixed");

if (instructions[0].OpCode != OpCodes.Ldarg_0 ||
    instructions[1].OpCode != OpCodes.Call ||
    !instructions[1].Operand.ToString()!.Contains("ManualSendGuard::Allow") ||
    instructions[2].OpCode != OpCodes.Brtrue_S ||
    instructions[3].OpCode != OpCodes.Ret ||
    !ReferenceEquals(instructions[2].Operand, instructions[4]))
    throw new Exception("Missing guard-before-async preflight or incorrect branch");

for (int i = 4; i < instructions.Count; i++)
{
    var old = legacy.Body.Instructions[i - 4];
    var now = instructions[i];
    if (old.OpCode != now.OpCode)
        throw new Exception($"Changed original instruction opcode at {i - 4}");
}

var stateType = "PS4RemotePkgMobile.MainPage/<BtnInjetar_Clicked>d__13";
var oldState = original.MainModule.GetType(stateType).Methods.Single(m=>m.Name=="MoveNext");
var newState = patched.MainModule.GetType(stateType).Methods.Single(m=>m.Name=="MoveNext");
if (oldState.Body.Instructions.Count != newState.Body.Instructions.Count ||
    oldState.Body.Instructions.Where((t,i)=>t.OpCode!=newState.Body.Instructions[i].OpCode).Any())
    throw new Exception("Legacy sender state machine changed");

if (original.MainModule.Mvid != patched.MainModule.Mvid)
    throw new Exception("Original managed module MVID changed");

Console.WriteLine("RESULT: 5 manual sender IL safety checks passed");
