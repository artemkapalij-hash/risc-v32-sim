using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Frozen;

namespace Riscv32Sim {

    readonly record struct Instruction {

        readonly int opcode;
        readonly int rd;
        readonly int rs1;
        readonly int rs2;
        readonly int imm;

        public Instruction(int opcode, int rd=0, int rs1=0, int rs2=0, int imm=0) {
            this.opcode = opcode;
            this.rd = rd;
            this.rs1 = rs1;
            this.rs2 = rs2;
            this.imm = imm;
        }

        public override string ToString() {
            return $"{opcode} {rd} {rs1} {rs2} {imm}";
        }

    };
    

    // sealed keyword disallows inheritance, which communicates intent 
    // behind the class and allows for some optimisations
    sealed class PseudoAssembler {

        private FrozenDictionary<string, string> _instructionTypes = new Dictionary<string, string>

        {

            // These groups have the same argument structure, but not necessarily the same functions,
            // so if most (but not necessarily all) of instructions of the group share a
            // similar function, it will be noted

            // R: (rd, rs1, rs2) (registers only instruction)
            { "add", "R" },
            { "sub", "R" },
            { "sll", "R" },
            { "slt", "R" },
            { "sltu", "R" },
            { "xor", "R" },
            { "srl", "R" },
            { "sra", "R" },
            { "or", "R" },
            { "and", "R" },

            // IA: (rd, rs1, imm) (operation between an immediate value and a register)
            { "addi", "IA" },
            { "slti", "IA" },
            { "sltiu", "IA" },
            { "xori", "IA" },
            { "ori", "IA" },
            { "andi", "IA" },
            { "slli", "IA" },
            { "srli", "IA" },
            { "srai", "IA" },
            
            // IL: (rd, imm(rs1)) (most of them load something from the address imm + rs1 to rd)
            { "lb", "IL" },
            { "lh", "IL" },
            { "lw", "IL" },
            { "lbu", "IL" },
            { "lhu", "IL" },
            { "jalr", "IL" },

            // IN: no operands
            { "ecall", "IN" },
            { "ebreak", "IN" },

            // S: (rs2, imm(rs1)) (store contents of rs2 into imm + rs1)
            { "sb",    "S" },
            { "sh",    "S" },
            { "sw",    "S" },

            // B: (rs1, rs2, label) (branch on a positive result of a comparison of rs1 and rs2)
            { "beq",   "B" },
            { "bne",   "B" },
            { "blt",   "B" },
            { "bge",   "B" },
            { "bltu",  "B" },
            { "bgeu",  "B" },

            // U: (rd, imm) (imm into the upper bits of rd)
            { "lui",   "U" },
            { "auipc", "U" },

            // J: (rd, label) (jump to a label unconditionally, save the address after the one
            // from which the jump was performed into rd)
            { "jal",   "J" },

        }.ToFrozenDictionary();

        private FrozenDictionary<string, int> _registers = new Dictionary<string, int>

        {

            // Numeric names
            { "x0", 0 },   { "x1", 1 },   { "x2", 2 },   { "x3", 3 },
            { "x4", 4 },   { "x5", 5 },   { "x6", 6 },   { "x7", 7 },
            { "x8", 8 },   { "x9", 9 },   { "x10", 10 }, { "x11", 11 },
            { "x12", 12 }, { "x13", 13 }, { "x14", 14 }, { "x15", 15 },
            { "x16", 16 }, { "x17", 17 }, { "x18", 18 }, { "x19", 19 },
            { "x20", 20 }, { "x21", 21 }, { "x22", 22 }, { "x23", 23 },
            { "x24", 24 }, { "x25", 25 }, { "x26", 26 }, { "x27", 27 },
            { "x28", 28 }, { "x29", 29 }, { "x30", 30 }, { "x31", 31 },

            // ABI names
            { "zero", 0 },
            { "ra", 1 },
            { "sp", 2 },
            { "gp", 3 },
            { "tp", 4 },
            { "t0", 5 },  { "t1", 6 },  { "t2", 7 },
            { "s0", 8 },  { "fp", 8 },
            { "s1", 9 },
            { "a0", 10 }, { "a1", 11 }, { "a2", 12 }, { "a3", 13 },
            { "a4", 14 }, { "a5", 15 }, { "a6", 16 }, { "a7", 17 },
            { "s2", 18 }, { "s3", 19 }, { "s4", 20 }, { "s5", 21 },
            { "s6", 22 }, { "s7", 23 }, { "s8", 24 }, { "s9", 25 },
            { "s10", 26 }, { "s11", 27 },
            { "t3", 28 }, { "t4", 29 }, { "t5", 30 }, { "t6", 31 },

        }.ToFrozenDictionary();

        // If instruction name is in this dictionary, do
        // string.Format(_pseudoInstructions[instruction], instructionParameters)
        // to convert
        private FrozenDictionary<string, string> _pseudoInstructions = new Dictionary<string, string>

        {

            // copies and arithmetic
            { "nop",  "\taddi\tzero,zero,0" },
            { "mv",   "\taddi\t{0},{1},0" },
            { "not",  "\txori\t{0},{1},-1" },
            { "neg",  "\tsub\t{0},zero,{1}" },

            // set-if comparisons
            { "seqz", "\tsltiu\t{0},{1},1" },
            { "snez", "\tsltu\t{0},zero,{1}" },
            { "sltz", "\tslt\t{0},{1},zero" },
            { "sgtz", "\tslt\t{0},zero,{1}" },

            // branches against zero
            { "beqz", "\tbeq\t{0},zero,{1}" },
            { "bnez", "\tbne\t{0},zero,{1}" },
            { "blez", "\tbge\tzero,{0},{1}" },
            { "bgez", "\tbge\t{0},zero,{1}" },
            { "bltz", "\tblt\t{0},zero,{1}" },
            { "bgtz", "\tblt\tzero,{0},{1}" },

            // branches with swapped operands
            { "bgt",  "\tblt\t{1},{0},{2}" },
            { "ble",  "\tbge\t{1},{0},{2}" },
            { "bgtu", "\tbltu\t{1},{0},{2}" },
            { "bleu", "\tbgeu\t{1},{0},{2}" },

            // jumps, calls and returns
            { "j",    "\tjal\tzero,{0}" },
            { "jr",   "\tjalr\tzero,0({0})" },
            { "ret",  "\tjalr\tzero,0(ra)" },
            { "call", "\tjal\tra,{0}" },
            { "tail", "\tjal\tzero,{0}" },

        }.ToFrozenDictionary();

        private FrozenDictionary<string, int> _opcodes = new Dictionary<string, int>

        {

            // R
            { "add",   1 },
            { "sub",   2 },
            { "sll",   3 },
            { "slt",   4 },
            { "sltu",  5 },
            { "xor",   6 },
            { "srl",   7 },
            { "sra",   8 },
            { "or",    9 },
            { "and",  10 },

            // IA
            { "addi",  11 },
            { "slti",  12 },
            { "sltiu", 13 },
            { "xori",  14 },
            { "ori",   15 },
            { "andi",  16 },
            { "slli",  17 },
            { "srli",  18 },
            { "srai",  19 },

            // IL
            { "lb",    20 },
            { "lh",    21 },
            { "lw",    22 },
            { "lbu",   23 },
            { "lhu",   24 },
            { "jalr",  25 },

            // IN
            { "ecall",  26 },
            { "ebreak", 27 },

            // S
            { "sb",    28 },
            { "sh",    29 },
            { "sw",    30 },

            // B
            { "beq",   31 },
            { "bne",   32 },
            { "blt",   33 },
            { "bge",   34 },
            { "bltu",  35 },
            { "bgeu",  36 },

            // U
            { "lui",   37 },
            { "auipc", 38 },

            // J
            { "jal",   39 },

        }.ToFrozenDictionary();

        private readonly string _inFile;
        private readonly string _pseudoHandled;
        private readonly string _labelsHandled;
        private readonly string _outFile;

        public PseudoAssembler(string inFile, string pseudoHandled, string labelsHandled, string outFile) {
            _inFile = inFile;
            _pseudoHandled = pseudoHandled;
            _labelsHandled = labelsHandled;
            _outFile = outFile;
        }

        // R: rd,rs1,rs2
        private Instruction ConvertInstructionR(string instructionStr) {
            string[] tokens = instructionStr.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
            int opcode = _opcodes[tokens[0]];
            int rd = _registers[tokens[1]];
            int rs1 = _registers[tokens[2]];
            int rs2 = _registers[tokens[3]];
            return new Instruction(opcode: opcode, rd: rd, rs1: rs1, rs2: rs2); 
        }

        // IA: rd,rs1,imm
        private Instruction ConvertInstructionIA(string instructionStr) {
            string[] tokens = instructionStr.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
            int opcode = _opcodes[tokens[0]];
            int rd = _registers[tokens[1]];
            int rs1 = _registers[tokens[2]];
            int imm = int.Parse(tokens[3]);
            return new Instruction(opcode: opcode, rd: rd, rs1: rs1, imm: imm);
        }

        // IL: rd,imm(rs1)
        private Instruction ConvertInstructionIL(string instructionStr) {
            string[] tokens = instructionStr.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
            int opcode = _opcodes[tokens[0]];
            int rd = _registers[tokens[1]];
            // "12(sp)" -> ["12", "sp"]
            string[] memory = tokens[2].Split(['(', ')'], StringSplitOptions.RemoveEmptyEntries);
            int imm = int.Parse(memory[0]);
            int rs1 = _registers[memory[1]];
            return new Instruction(opcode: opcode, rd: rd, rs1: rs1, imm: imm);
        }

        // IN: no operands
        private Instruction ConvertInstructionIN(string instructionStr) {
            string[] tokens = instructionStr.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
            int opcode = _opcodes[tokens[0]];
            return new Instruction(opcode: opcode);
        }

        // S: rs2,imm(rs1)
        private Instruction ConvertInstructionS(string instructionStr) {
            string[] tokens = instructionStr.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
            int opcode = _opcodes[tokens[0]];
            int rs2 = _registers[tokens[1]];
            // "12(sp)" -> ["12", "sp"]
            string[] memory = tokens[2].Split(['(', ')'], StringSplitOptions.RemoveEmptyEntries);
            int imm = int.Parse(memory[0]);
            int rs1 = _registers[memory[1]];
            return new Instruction(opcode: opcode, rs1: rs1, rs2: rs2, imm: imm);
        }

        // B: rs1,rs2,offset (label already resolved)
        private Instruction ConvertInstructionB(string instructionStr) {
            string[] tokens = instructionStr.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
            int opcode = _opcodes[tokens[0]];
            int rs1 = _registers[tokens[1]];
            int rs2 = _registers[tokens[2]];
            int imm = int.Parse(tokens[3]);
            return new Instruction(opcode: opcode, rs1: rs1, rs2: rs2, imm: imm);
        }

        // U: rd,imm
        private Instruction ConvertInstructionU(string instructionStr) {
            string[] tokens = instructionStr.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
            int opcode = _opcodes[tokens[0]];
            int rd = _registers[tokens[1]];
            int imm = int.Parse(tokens[2]);
            return new Instruction(opcode: opcode, rd: rd, imm: imm);
        }

        // J: rd,offset (label already resolved)
        private Instruction ConvertInstructionJ(string instructionStr) {
            string[] tokens = instructionStr.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
            int opcode = _opcodes[tokens[0]];
            int rd = _registers[tokens[1]];
            int imm = int.Parse(tokens[2]);
            return new Instruction(opcode: opcode, rd: rd, imm: imm);
        }

        /* Naive split: 
         * li rd,6144 (or 0x1800) 
         * ->
         * hi = 0x1800 >> 12 = 1, 
         * lo = 0x1800 & 0xFFF = 0x800 
         * ->
         * lui rd,1 -> rd = 0x1000
         * addi rd,rd,0x800
         * 0x800 is sign-extended to -2048, so instead of 6144 it loads
         * 2048, the solution is a smarter split, that is:
         * hi = (imm + 0x800) >> 12 = 2
         * lo = imm - (hi << 12) = -2048
         * Essentially, lo is a correction between -2048 and 2047
         */
        static private void Split(int imm, out int hi, out int lo) {
            hi = ((imm + 0x800) >> 12);
            lo = imm - (hi << 12);
        }

        public void Assemble() {

            string[] instructionsFile = File.ReadAllLines(_inFile);
            List<string> instructions = new List<string>(instructionsFile);

            // Converts pseudo instructions into actual instructions
            // not rd rs1 -> xor  rd rs1 -1 (-1 = 0xFFFFFFFF) 
            // mv  rd rs1 -> addi rd rs1 0
            for (int i = 0; i < instructions.Count; i++) {
                string[] tokens = instructions[i].Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) {
                    continue;
                }
                string operation = tokens[0];
                string[] operands = tokens[1..];
                bool isPseudo = _pseudoInstructions.TryGetValue(operation, out string? format);
                if (isPseudo) {
                    instructions[i] = string.Format(format!, operands);
                }

                if (operation == "li") {
                    // This will fail, if input assembly has hex numbers in it, 
                    // this program is designed to handle the gcc-generated assembly
                    string rd = operands[0];
                    string immStr = operands[1];
                    bool parsed  = int.TryParse(immStr, out int imm);
                    if (parsed) {
                        if (imm >= -2048 && imm <= 2047) {
                            instructions[i] = $"\taddi\t{rd},zero,{immStr}";
                        }
                        else {
                            Split(imm, out int hi, out int lo);
                            instructions[i] = $"\tlui\t{rd},{hi}\n\taddi\t{rd},{rd},{lo}";
                        }
                    }
                }

                if (operation == "la" || operation == "lla") {
                    string rd = operands[0];
                    string label = operands[1];
                    instructions[i] = $"\tauipc\t{rd},%label_hi({label})\n\taddi\t{rd},{rd},%label_lo({label})";
                }
            }

            instructions = instructions.SelectMany(line => line.Split('\n')).ToList();
            File.WriteAllLines(_pseudoHandled, instructions);

            Dictionary<string, int> addressesOfLabels = new();
            int count = 0;
            foreach (string instruction in instructions) {
                if (instruction.Trim()[^1] == ':') {
                    string label = instruction[..^1];
                    addressesOfLabels[label] = count;
                }
                else if (instruction.Trim()[0] == '.') {
                    continue;
                }
                else {
                    count += 4;
                }
            }

            instructions.RemoveAll(line => line.Trim().StartsWith('.') || line.Trim().EndsWith(':'));

            count = 0;
            List<string> labelIsThirdOperand = ["beq", "bne", "blt", "bge", "bltu", "bgeu"];
            List<string> labelIsSecondOperand = ["jal"] ;
            // DOES NOT handle %label_hi and %label_lo
            for (int i = 0; i < instructions.Count; i++) {
                string[] tokens = instructions[i].Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) {
                    continue;
                }
                string operation = tokens[0];
                string[] operands = tokens[1..];

                if (labelIsThirdOperand.Contains(operation)) {
                    int instructionAddress = i*4;
                    string label = operands[2];
                    int labelAddress = addressesOfLabels[label];
                    int relativeJump = labelAddress - instructionAddress;
                    instructions[i] = $"\t{operation}\t{operands[0]},{operands[1]},{relativeJump}";
                }
                if (labelIsSecondOperand.Contains(operation)) {
                    int instructionAddress = i*4;
                    string label = operands[1];
                    int labelAddress = addressesOfLabels[label];
                    int relativeJump = labelAddress - instructionAddress;
                    instructions[i] = $"\t{operation}\t{operands[0]},{relativeJump}";
                }
            }

            File.WriteAllLines(_labelsHandled, instructions);

            for (int i = 0; i < instructions.Count; i++) {
                string instructionStr = instructions[i];
                string[] tokens = instructionStr.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
                string name = tokens[0];
                Instruction instruction;
                switch (_instructionTypes[name]) {
                    case "R": instruction = ConvertInstructionR(instructionStr); break; 
                    case "IA": instruction = ConvertInstructionIA(instructionStr); break; 
                    case "IL": instruction = ConvertInstructionIL(instructionStr); break; 
                    case "IN": instruction = ConvertInstructionIN(instructionStr); break; 
                    case "S": instruction = ConvertInstructionS(instructionStr); break;
                    case "B": instruction = ConvertInstructionB(instructionStr); break;
                    case "U": instruction = ConvertInstructionU(instructionStr); break;
                    case "J": instruction = ConvertInstructionJ(instructionStr); break;
                    default: throw new InvalidOperationException($"Unknown instruction: {name}");

                }
                instructions[i] = instruction.ToString();
            }

            File.WriteAllLines(_outFile, instructions);

        }

    }

    /*/
    static class VM {

    }
    /*/

    public class Top {

        public static void Main(string[] args) {
            
            PseudoAssembler assembler = new("in.s", "interm.p.s", "interm.l.s", "prog.rvm");
            assembler.Assemble();

        }

    }

}
