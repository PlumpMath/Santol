﻿using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MMethodDefinition = Mono.Cecil.MethodDefinition;

namespace Santol
{
    public class MethodDefinition
    {
        public string Name => Definition.Name;
        public MMethodDefinition Definition { get; }
        public MethodBody Body { get; }
        public ILProcessor Processor { get; }
        public bool DoesReturn => Definition.ReturnType.MetadataType != MetadataType.Void;
        public IList<CodeSegment> Segments { get; set; }


        public IList<VariableDefinition> Locals { get; }

        public MethodDefinition(MMethodDefinition definition)
        {
            Definition = definition;
            Body = definition.Body;
            Processor = Body.GetILProcessor();
            Locals = new List<VariableDefinition>();
        }

        public void AddLocal(VariableDefinition variable)
        {
            Locals.Add(variable);
        }

        public void PrintInstructions()
        {
            //Find all jump destinations
            IList<Instruction> jumpDestinations = GetJumpDestinations();

            Console.WriteLine("  Instuctions:");
            foreach (Instruction instruction in Body.Instructions)
            {
                if (jumpDestinations.Contains(instruction))
                {
                    Console.WriteLine($"    JUMP_POINT:");
                }
                Console.WriteLine("      " + instruction);

                if (instruction.OpCode.FlowControl == FlowControl.Phi)
                    throw new Exception($"Phi: {instruction}");

                if (instruction.OpCode.FlowControl == FlowControl.Break)
                    throw new Exception($"Break: {instruction}");
            }
        }

        public void PrintSegments()
        {
            Console.WriteLine("  Segments:");
            foreach (CodeSegment segment in Segments)
            {
                Console.WriteLine($"    {segment.Name}:");
                Console.WriteLine($"      Force No Incomings: {segment.ForceNoIncomings}");
                Console.WriteLine($"      Incoming Size: {segment.IncomingSize}");
                Console.WriteLine($"      Calls: {segment.Calls.Count}");
                Console.WriteLine($"      End Point: {segment.IsEndPoint}");
                foreach (Instruction instruction in segment.Instructions)
                {
                    Console.WriteLine("        " + instruction);
                    //                    Console.WriteLine("        " + instruction + " Pops " + CalculatePopSize(instruction, methodReturns));
                }
            }
        }

        public int FixMidBranches()
        {
            int @fixed = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                //Find all mid jumps
                IList<Instruction> midJumps = new List<Instruction>();
                foreach (Instruction instruction in Body.Instructions)
                {
                    OpCode code = instruction.OpCode;
                    if (code.FlowControl == FlowControl.Cond_Branch && instruction.Next != null &&
                        instruction.Next.OpCode.FlowControl != FlowControl.Branch)
                    {
                        midJumps.Add(instruction);
                    }
                }

                //Insert fixed jump
                foreach (Instruction instruction in midJumps)
                    Processor.InsertAfter(instruction, Processor.Create(OpCodes.Br, instruction.Next));

                //Ensure first pass fixed all mid jumps
                if (pass == 0)
                    @fixed = midJumps.Count;
                else if (midJumps.Count  != 0)
                    throw new NotSupportedException("Unable to break up branches");
            }
            return @fixed;
        }

        private IList<Instruction> GetJumpDestinations()
        {
            IList<Instruction> jumpDestinations = new List<Instruction>();
            foreach (Instruction instruction in Body.Instructions)
            {
                OpCode code = instruction.OpCode;
                if (code.FlowControl == FlowControl.Branch || code.FlowControl == FlowControl.Cond_Branch)
                {
                    if (code.OperandType == OperandType.ShortInlineBrTarget ||
                        code.OperandType == OperandType.InlineBrTarget)
                        jumpDestinations.AddOpt((Instruction) instruction.Operand);
                    else if (code.OperandType == OperandType.InlineSwitch)
                        jumpDestinations.AddOpt((Instruction[]) instruction.Operand);
                    else
                        throw new NotImplementedException("Unknown branch instruction " + instruction + "  " +
                                                          instruction.Operand.GetType());
                }
            }

            Instruction firstInst = Body.Instructions.Count > 0 ? Body.Instructions[0] : null;
            if (firstInst != null && !jumpDestinations.Contains(firstInst))
                jumpDestinations.Add(firstInst);

            return jumpDestinations;
        }

        public int FixFallthroughs()
        {
            int @fixed = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                //Find all jump destinations
                IList<Instruction> jumpDestinations = GetJumpDestinations();

                //Find all end points
                List<Instruction> endPoints = new List<Instruction>();
                foreach (Instruction instruction in Body.Instructions)
                    if (jumpDestinations.Contains(instruction) && instruction.Previous != null)
                        endPoints.Add(instruction.Previous);


                //Checks for fallthroughs
                int fixCount = 0;
                foreach (Instruction instruction in endPoints)
                {
                    OpCode code = instruction.OpCode;
                    if (code.FlowControl == FlowControl.Break)
                        throw new NotImplementedException("Breaks have not been checked");

                    if (code.FlowControl != FlowControl.Branch && code.FlowControl != FlowControl.Return)
                    {
                        //Fixes fallthrough
                        Processor.InsertAfter(instruction, Processor.Create(OpCodes.Br, instruction.Next));
                        fixCount++;
                    }
                }

                //Ensure first pass fixed fallthroughs
                if (pass == 0)
                    @fixed = fixCount;
                else if (fixCount != 0)
                    throw new NotSupportedException("Unable to fix fallthroughs");
            }
            return @fixed;
        }

        public void GenerateSegments()
        {
            IList<Instruction> jumpDestinations = GetJumpDestinations();
            Segments = new List<CodeSegment>();

            int segmentLId = 0;
            CodeSegment currentSegment = null;
            foreach (Instruction instruction in Body.Instructions)
            {
                if (jumpDestinations.Contains(instruction))
                {
                    currentSegment = new CodeSegment(this, "SEG_" + (segmentLId++));
                    Segments.Add(currentSegment);
                }
                if (currentSegment == null)
                    throw new NotSupportedException(
                        "Method body is invalid! Unsure how to handle instructions outside segment");
                currentSegment.AddInstruction(instruction);
            }
        }

        public CodeSegment GetSegment(Instruction instruction)
        {
            foreach (CodeSegment segment in Segments)
            {
                if (segment.Instructions.Contains(instruction))
                    return segment;
            }
            return null;
        }

        public void DetectNoIncomings()
        {
            IList<Instruction> jumpDestinations = new List<Instruction>();
            foreach (Instruction instruction in Body.Instructions)
            {
                OpCode code = instruction.OpCode;
                if (code.FlowControl == FlowControl.Branch || code.FlowControl == FlowControl.Cond_Branch)
                {
                    switch (code.OperandType)
                    {
                        case OperandType.ShortInlineBrTarget:
                        case OperandType.InlineBrTarget:
                            jumpDestinations.AddOpt((Instruction) instruction.Operand);
                            break;
                        case OperandType.InlineSwitch:
                            jumpDestinations.AddOpt((Instruction[]) instruction.Operand);
                            break;
                        default:
                            throw new NotImplementedException("Unknown branch instruction " + instruction + "  " +
                                                              instruction.Operand.GetType());
                    }
                }
                else if (instruction.Previous != null && instruction.Previous.OpCode.FlowControl == FlowControl.Branch &&
                         !jumpDestinations.Contains(instruction))
                    GetSegment(instruction).ForceNoIncomings = true;
            }
        }
    }
}