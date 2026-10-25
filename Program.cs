using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Buffers.Binary;

namespace Riscv32Sim {
    
    /* This is used while generating bytecode. This is a representation
     * of a single instruction as it is present in bytecode. Not 
     * every field is used by a particular instruction, like
     * add uses only rd, rs1, rs2 or ebreak has none at all.
     * Unused fields must be assigned 0 to and be ignored by the runtime 
     */
    readonly record struct Instruction {

        public readonly int opcode;
        public readonly int rd;
        public readonly int rs1;
        public readonly int rs2;
        public readonly int imm;

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

        /* Converts pseudo instructions into actual instructions
         * not rd rs1 -> xori rd rs1 -1 (-1 = 0xFFFFFFFF) 
         * mv  rd rs1 -> addi rd rs1 0
         */
        private void ExpandPseudoInstructions(List<string> instructions) {

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
           
        }
 
        /* Makes a dictionary of addresses to which all the labels in the 
         * file go. Used in the label-to-jump substitution step 
         */
        private static void GetAddressesOfLabels(List<string> instructions, Dictionary<string, int> addressesOfLabels) {

            int count = 0;
            foreach (string instruction in instructions) {
                if (instruction.Trim()[^1] == ':') {
                    string label = instruction.Trim()[..^1];
                    addressesOfLabels[label] = count;
                }
                else if (instruction.Trim()[0] == '.') {
                    continue;
                }
                else {
                    count += 4;
                }
            }

        }

        /* Substitutes all the labels for relative jumps, since labels are
         * an assembly abstraction. At this point, instructions are basically
         * the bytecode we want, it just needs to be converted into numbers
         */
        private static void SubstituteLabelsForJumps(List<string> instructions, Dictionary<string, int> addressesOfLabels) {

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

        }
        
        /* Converts instructions from List<string> instructions into numbers,
         * computers can handle them more efficiently after all
         */
        public void ConvertStringInstructionsToIntegers(List<string> instructions) {

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

        }
        
        /* A wrapper, that orchestrates transformation from gcc-generated assembly
         * to the bytecode a VM can use via the following pipeline:
         * Expand pseudo-instructions -> firure out what addresses labels point to ->
         * -> substitute labels for relative jumps -> convert it all to integers 
         */
        public void Assemble() {

            string[] instructionsFile = File.ReadAllLines(_inFile);
            List<string> instructions = new List<string>(instructionsFile);
            // https://jborza.com/post/2021-05-11-riscv-linux-syscalls/
            // 93 is the syscall for exit, stored into a7 register
            instructions.Insert(0, "\tecall");
            instructions.Insert(0, "\tli\ta7,93");
            instructions.Insert(0, "\tcall\tmain");
        
            ExpandPseudoInstructions(instructions);

            instructions = instructions.SelectMany(line => line.Split('\n')).ToList();
            File.WriteAllLines(_pseudoHandled, instructions);

            Dictionary<string, int> addressesOfLabels = new();
            GetAddressesOfLabels(instructions, addressesOfLabels);

            instructions.RemoveAll(line => line.Trim().StartsWith('.') || 
                                   line.Trim().EndsWith(':') || 
                                   string.IsNullOrWhiteSpace(line));

            SubstituteLabelsForJumps(instructions, addressesOfLabels);

            File.WriteAllLines(_labelsHandled, instructions);

            ConvertStringInstructionsToIntegers(instructions);

            File.WriteAllLines(_outFile, instructions);

        }

    }

    sealed class RV32VM {

        public enum Opcode

        {

            INVALID = 0,

            // R
            ADD  = 1,
            SUB  = 2,
            SLL  = 3,
            SLT  = 4,
            SLTU = 5,
            XOR  = 6,
            SRL  = 7,
            SRA  = 8,
            OR   = 9,
            AND  = 10,

            // IA
            ADDI  = 11,
            SLTI  = 12,
            SLTIU = 13,
            XORI  = 14,
            ORI   = 15,
            ANDI  = 16,
            SLLI  = 17,
            SRLI  = 18,
            SRAI  = 19,

            // IL
            LB   = 20,
            LH   = 21,
            LW   = 22,
            LBU  = 23,
            LHU  = 24,
            JALR = 25,

            // IN
            ECALL  = 26,
            EBREAK = 27,

            // S
            SB = 28,
            SH = 29,
            SW = 30,

            // B
            BEQ  = 31,
            BNE  = 32,
            BLT  = 33,
            BGE  = 34,
            BLTU = 35,
            BGEU = 36,

            // U
            LUI   = 37, 
            AUIPC = 38,

            // J
            JAL = 39,

        }

        private byte[] _RAM;
        private int[] _registers;
        private readonly int _registersCount;
        private readonly int _stackPointerRegister;
        private int _pc;
        private readonly int _RAMSize;
        private readonly string _inFile;

        public RV32VM(int RAMSize, string inFile) {
            _registersCount = 32;
            _stackPointerRegister = 2;
            _RAMSize = RAMSize;
            _registers = new int[_registersCount];
            _RAM = new byte[_RAMSize];
            _inFile = inFile;
        }

        private List<Instruction> GetAllInstructions() {
            List<string> instructionsStr = File.ReadAllLines(_inFile).ToList();
            List<Instruction> instructionsActual = new();
            foreach (string instruction in instructionsStr) {
                int[] fields = Array.ConvertAll(
                    instruction.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                    int.Parse);
                Instruction newInstruction = new(fields[0], fields[1], fields[2], fields[3], fields[4]);
                instructionsActual.Add(newInstruction);
            }
            return instructionsActual;
        }

        private bool AccessValid(int address, int offset) {
            return address + offset <= _RAMSize && address >= 0;
        }

        private int ReadByte(int address, bool isSigned) {
            if (!AccessValid(address, 1)) {
                throw new ArgumentOutOfRangeException(nameof(address), $"Access of 1 byte on address {address} at pc {_pc}");
            }
            return isSigned ? (sbyte)_RAM[address] : _RAM[address];
        }

        private int ReadHalf(int address, bool isSigned) {
            if (!AccessValid(address, 2)) {
                throw new ArgumentOutOfRangeException(nameof(address), $"Access of 2 bytes on address {address} at pc {_pc}");
            }
            ReadOnlySpan<byte> half = _RAM.AsSpan(address, 2);
            return isSigned ? BinaryPrimitives.ReadInt16LittleEndian(half) : BinaryPrimitives.ReadUInt16LittleEndian(half);
        }

        private int ReadWord(int address) {
            if (!AccessValid(address, 4)) {
                throw new ArgumentOutOfRangeException(nameof(address), $"Access of 4 bytes on address {address} at pc {_pc}");
            }
            ReadOnlySpan<byte> word = _RAM.AsSpan(address, 4);
            return BinaryPrimitives.ReadInt32LittleEndian(word);
        }

        private void WriteByte(int address, byte write) {
            if (!AccessValid(address, 1)) {
                throw new ArgumentOutOfRangeException(nameof(address), $"Access of 1 byte on address {address} at pc {_pc}");
            }
            _RAM[address] = write;
        }

        private void WriteHalf(int address, short write) {
            if (!AccessValid(address, 2)) {
                throw new ArgumentOutOfRangeException(nameof(address), $"Access of 2 bytes on address {address} at pc {_pc}");
            }
            BinaryPrimitives.WriteInt16LittleEndian(_RAM.AsSpan(address, 2), (short)write);
        }

        private void WriteWord(int address, int write) {
            if (!AccessValid(address, 4)) {
                throw new ArgumentOutOfRangeException(nameof(address), $"Access of 4 bytes on address {address} at pc {_pc}");
            }
            BinaryPrimitives.WriteInt32LittleEndian(_RAM.AsSpan(address, 4), write);
        }

        public void Execute() {

            List<Instruction> instructions = GetAllInstructions();
            _registers[_stackPointerRegister] = (_RAMSize - (_RAMSize % 8)) - 32; // Good enough for now
            _pc = 0;
            
            while (true) {
                
                Instruction instruction = instructions[_pc / 4];

                _registers[0] = 0;
                int rs1 = _registers[instruction.rs1];
                int rs2 = _registers[instruction.rs2];
                int imm = instruction.imm;
                ref int rd = ref _registers[instruction.rd];

                switch ((Opcode)instruction.opcode) {
                    
                    // R
                    case Opcode.ADD:  rd = rs1 + rs2; _pc += 4; break;
                    case Opcode.SUB:  rd = rs1 - rs2; _pc += 4; break;
                    case Opcode.SLL:  rd = (int)((uint)rs1 << (rs2 & 0b11111)); _pc += 4; break;
                    case Opcode.SLT:  rd = rs1 < rs2 ? 1 : 0; _pc += 4; break;
                    case Opcode.SLTU: rd = (uint)rs1 < (uint)rs2 ? 1 : 0; _pc += 4; break;
                    case Opcode.XOR:  rd = rs1 ^ rs2; _pc += 4; break;
                    case Opcode.SRL:  rd = (int)((uint)rs1 >> (rs2 & 0b11111)); _pc += 4; break;
                    case Opcode.SRA:  rd = rs1 >> (rs2 & 0b11111); _pc += 4; break;
                    case Opcode.OR:   rd = rs1 | rs2; _pc += 4; break;
                    case Opcode.AND:  rd = rs1 & rs2; _pc += 4; break;

                    // IA
                    case Opcode.ADDI:  rd = rs1 + imm; _pc += 4; break;
                    case Opcode.SLTI:  rd = rs1 < imm ? 1 : 0; _pc += 4; break;
                    case Opcode.SLTIU: rd = (uint)rs1 < (uint)imm ? 1 : 0; _pc += 4; break;
                    case Opcode.XORI:  rd = rs1 ^ imm; _pc += 4; break;
                    case Opcode.ORI:   rd = rs1 | imm; _pc += 4; break;
                    case Opcode.ANDI:  rd = rs1 & imm; _pc += 4; break;
                    case Opcode.SLLI:  rd = (int)((uint)rs1 << (imm & 0b11111)); _pc += 4; break;
                    case Opcode.SRLI:  rd = (int)((uint)rs1 >> (imm & 0b11111)); _pc += 4; break;
                    case Opcode.SRAI:  rd = rs1 >> (imm & 0b11111); _pc += 4; break;

                    // IL
                    case Opcode.LB:   rd = ReadByte(rs1+imm, isSigned: true);  _pc += 4; break;
                    case Opcode.LH:   rd = ReadHalf(rs1+imm, isSigned: true);  _pc += 4; break;
                    case Opcode.LW:   rd = ReadWord(rs1+imm);                  _pc += 4; break;
                    case Opcode.LBU:  rd = ReadByte(rs1+imm, isSigned: false); _pc += 4; break;
                    case Opcode.LHU:  rd = ReadHalf(rs1+imm, isSigned: false); _pc += 4; break;
                    case Opcode.JALR: rd = _pc + 4; _pc = (rs1 + imm) & ~1;              break;
                    
                    // TODO: Make proper ecall and ebreak
                    // IN
                    case Opcode.ECALL:  Console.WriteLine($"The program {_inFile} returned with exit code {_registers[10]}"); return;
                    case Opcode.EBREAK: Console.WriteLine($"Breakpoint at {_pc}"); return;

                    // S
                    case Opcode.SB: WriteByte(rs1 + imm, (byte)rs2);  _pc += 4; break;
                    case Opcode.SH: WriteHalf(rs1 + imm, (short)rs2); _pc += 4; break;
                    case Opcode.SW: WriteWord(rs1 + imm, rs2);        _pc += 4; break;

                    // B
                    case Opcode.BEQ:  _pc += (rs1 == rs2) ? imm : 4; break;
                    case Opcode.BNE:  _pc += (rs1 != rs2) ? imm : 4; break;
                    case Opcode.BLT:  _pc += (rs1 <  rs2) ? imm : 4; break;
                    case Opcode.BGE:  _pc += (rs1 >= rs2) ? imm : 4; break;
                    case Opcode.BLTU: _pc += ((uint)rs1 <  (uint)rs2) ? imm : 4; break;
                    case Opcode.BGEU: _pc += ((uint)rs1 >= (uint)rs2) ? imm : 4; break;

                    // U
                    case Opcode.LUI:   rd = (imm << 12);       _pc += 4; break;
                    case Opcode.AUIPC: rd = _pc + (imm << 12); _pc += 4; break;

                    // J
                    case Opcode.JAL: rd = _pc + 4; _pc += imm; break;
                    
                    default: throw new InvalidOperationException($"Unrecognized opcode {instruction.opcode}");

                }

            }
            
        }

    }

    public class Top {

        public static void Main(string[] args) {
            
            PseudoAssembler assembler = new("test-cases/gcd/prog.s", "out/interm.pi.s", "out/interm.lbl.s", "out/prog.rv32bc");
            assembler.Assemble();
            RV32VM VM = new(16*1024*1024, "out/prog.rv32bc");
            VM.Execute();
        }

    }

}
