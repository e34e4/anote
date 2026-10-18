// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Based on the public-domain LZMA SDK specification by Igor Pavlov.

using System;

namespace SilentNotes.Services
{
    /// <summary>
    /// Standalone pure-C# LZMA1 stream decoder for decompressing AGContacts / Чей номер+
    /// (<c>callerid.mbagc.ru</c>) <c>ocidb</c> shard and accumulative files (13-byte header + raw LZMA stream).
    /// </summary>
    internal static class LzmaDecoder
    {
        private const int kNumStates = 12;
        private const int kNumPosSlotBits = 6;
        private const int kNumLenToPosStates = 4;
        private const int kMatchMinLen = 2;
        private const int kStartPosModelIndex = 4;
        private const int kEndPosModelIndex = 14;
        private const int kNumFullDistances = 1 << (kEndPosModelIndex >> 1);
        private const int kNumAlignBits = 4;
        private const int kAlignTableSize = 1 << kNumAlignBits;
        private const int kNumPosStatesMax = 1 << 4;
        private const int kNumLowLenBits = 3;
        private const int kNumMidLenBits = 3;
        private const int kNumHighLenBits = 8;
        private const int kNumLowLenSymbols = 1 << kNumLowLenBits;
        private const int kNumMidLenSymbols = 1 << kNumMidLenBits;
        private const int kNumLenSymbols = kNumLowLenSymbols + kNumMidLenSymbols + (1 << kNumHighLenBits);
        private const int kNumBitModelTotalBits = 11;
        private const uint kBitModelTotal = 1u << kNumBitModelTotalBits;
        private const int kNumMoveBits = 5;
        private const uint kTopValue = 1u << 24;

        /// <summary>
        /// Decompresses a standalone <c>.lzma</c> byte array (5-byte properties + 8-byte little-endian uncompressed size + stream).
        /// </summary>
        public static byte[] Decompress(byte[] input)
        {
            if (input == null || input.Length < 13)
                throw new InvalidOperationException("Input .lzma buffer is too short.");

            int d = input[0];
            if (d >= 9 * 5 * 5)
                throw new InvalidOperationException("Invalid LZMA properties byte.");

            int lc = d % 9;
            int rem = d / 9;
            int lp = rem % 5;
            int pb = rem / 5;

            long outSize = 0;
            for (int i = 0; i < 8; i++)
                outSize |= ((long)input[5 + i]) << (8 * i);

            if (outSize < 0 || outSize > 64 * 1024 * 1024)
                throw new InvalidOperationException("Unsupported LZMA uncompressed size.");

            byte[] output = new byte[(int)outSize];
            if (outSize == 0)
                return output;

            DecodeStream(input, 13, output, lc, lp, pb);
            return output;
        }

        private static void DecodeStream(byte[] src, int srcOffset, byte[] dst, int lc, int lp, int pb)
        {
            uint posStateMask = (1u << pb) - 1;
            uint lpMask = (1u << lp) - 1;
            int numLitStates = 1 << (lc + lp);

            ushort[] isMatch = InitProbs(kNumStates << 4);
            ushort[] isRep = InitProbs(kNumStates);
            ushort[] isRepG0 = InitProbs(kNumStates);
            ushort[] isRepG1 = InitProbs(kNumStates);
            ushort[] isRepG2 = InitProbs(kNumStates);
            ushort[] isRep0Long = InitProbs(kNumStates << 4);
            ushort[] posSlotDecoder = InitProbs(kNumLenToPosStates << kNumPosSlotBits);
            ushort[] posDecoders = InitProbs(kNumFullDistances - kEndPosModelIndex);
            ushort[] alignDecoder = InitProbs(kAlignTableSize);
            ushort[] literals = InitProbs(0x300 * numLitStates);

            var lenDecoder = new LenModel(1 << pb);
            var repLenDecoder = new LenModel(1 << pb);

            var rc = new RangeCoder(src, srcOffset);

            uint state = 0;
            uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
            int outPos = 0;
            int outLen = dst.Length;
            byte prevByte = 0;

            while (outPos < outLen)
            {
                uint posState = (uint)outPos & posStateMask;
                if (rc.DecodeBit(isMatch, (int)((state << 4) + posState)) == 0)
                {
                    int litState = (int)((((uint)outPos & lpMask) << lc) + ((uint)prevByte >> (8 - lc)));
                    int litBase = 0x300 * litState;
                    uint symbol = 1;
                    if (state >= 7)
                    {
                        byte matchByte = dst[outPos - (int)rep0 - 1];
                        do
                        {
                            uint matchBit = (uint)(matchByte >> 7) & 1;
                            matchByte <<= 1;
                            uint bit = rc.DecodeBit(literals, (int)(litBase + ((1 + matchBit) << 8) + symbol));
                            symbol = (symbol << 1) | bit;
                            if (matchBit != bit)
                            {
                                while (symbol < 0x100)
                                    symbol = (symbol << 1) | rc.DecodeBit(literals, (int)(litBase + symbol));
                                break;
                            }
                        }
                        while (symbol < 0x100);
                    }
                    else
                    {
                        do
                        {
                            symbol = (symbol << 1) | rc.DecodeBit(literals, (int)(litBase + symbol));
                        }
                        while (symbol < 0x100);
                    }

                    prevByte = (byte)symbol;
                    dst[outPos++] = prevByte;
                    state = UpdateStateLiteral(state);
                }
                else
                {
                    uint len;
                    if (rc.DecodeBit(isRep, (int)state) == 1)
                    {
                        if (rc.DecodeBit(isRepG0, (int)state) == 0)
                        {
                            if (rc.DecodeBit(isRep0Long, (int)((state << 4) + posState)) == 0)
                            {
                                state = state < 7 ? 9u : 11u;
                                prevByte = dst[outPos - (int)rep0 - 1];
                                dst[outPos++] = prevByte;
                                continue;
                            }
                        }
                        else
                        {
                            uint distance;
                            if (rc.DecodeBit(isRepG1, (int)state) == 0)
                            {
                                distance = rep1;
                            }
                            else
                            {
                                if (rc.DecodeBit(isRepG2, (int)state) == 0)
                                    distance = rep2;
                                else
                                {
                                    distance = rep3;
                                    rep3 = rep2;
                                }
                                rep2 = rep1;
                            }
                            rep1 = rep0;
                            rep0 = distance;
                        }
                        len = repLenDecoder.Decode(rc, posState) + kMatchMinLen;
                        state = state < 7 ? 8u : 11u;
                    }
                    else
                    {
                        rep3 = rep2;
                        rep2 = rep1;
                        rep1 = rep0;
                        len = kMatchMinLen + lenDecoder.Decode(rc, posState);
                        state = state < 7 ? 7u : 10u;

                        uint lenToPosState = len - kMatchMinLen;
                        if (lenToPosState >= kNumLenToPosStates)
                            lenToPosState = kNumLenToPosStates - 1;

                        uint posSlot = DecodeBitTree(rc, posSlotDecoder, (int)(lenToPosState << kNumPosSlotBits), kNumPosSlotBits);
                        if (posSlot >= kStartPosModelIndex)
                        {
                            int numDirectBits = (int)((posSlot >> 1) - 1);
                            rep0 = (2 | (posSlot & 1)) << numDirectBits;
                            if (posSlot < kEndPosModelIndex)
                            {
                                rep0 += ReverseDecode(rc, posDecoders, (int)(rep0 - posSlot - 1), numDirectBits);
                            }
                            else
                            {
                                rep0 += rc.DecodeDirectBits(numDirectBits - kNumAlignBits) << kNumAlignBits;
                                rep0 += ReverseDecode(rc, alignDecoder, 0, kNumAlignBits);
                                if (rep0 == 0xFFFFFFFFu)
                                    break;
                            }
                        }
                        else
                        {
                            rep0 = posSlot;
                        }
                    }

                    int copyLen = (int)Math.Min(len, (uint)(outLen - outPos));
                    int srcIdx = outPos - (int)rep0 - 1;
                    for (int i = 0; i < copyLen; i++)
                    {
                        prevByte = dst[srcIdx + i];
                        dst[outPos++] = prevByte;
                    }
                }
            }
        }

        private static uint UpdateStateLiteral(uint state)
        {
            if (state < 4) return 0;
            if (state < 10) return state - 3;
            return state - 6;
        }

        private static ushort[] InitProbs(int count)
        {
            var p = new ushort[count];
            ushort half = (ushort)(kBitModelTotal >> 1);
            for (int i = 0; i < count; i++)
                p[i] = half;
            return p;
        }

        private static uint DecodeBitTree(RangeCoder rc, ushort[] probs, int offset, int numBits)
        {
            uint m = 1;
            for (int i = 0; i < numBits; i++)
                m = (m << 1) + rc.DecodeBit(probs, offset + (int)m);
            return m - (1u << numBits);
        }

        private static uint ReverseDecode(RangeCoder rc, ushort[] probs, int offset, int numBits)
        {
            uint m = 1;
            uint symbol = 0;
            for (int i = 0; i < numBits; i++)
            {
                uint bit = rc.DecodeBit(probs, offset + (int)m);
                m = (m << 1) + bit;
                symbol |= bit << i;
            }
            return symbol;
        }

        private sealed class LenModel
        {
            private readonly ushort[] _choice = InitProbs(2);
            private readonly ushort[] _low;
            private readonly ushort[] _mid;
            private readonly ushort[] _high = InitProbs(1 << kNumHighLenBits);

            public LenModel(int numPosStates)
            {
                _low = InitProbs(numPosStates << kNumLowLenBits);
                _mid = InitProbs(numPosStates << kNumMidLenBits);
            }

            public uint Decode(RangeCoder rc, uint posState)
            {
                if (rc.DecodeBit(_choice, 0) == 0)
                    return DecodeBitTree(rc, _low, (int)(posState << kNumLowLenBits), kNumLowLenBits);
                if (rc.DecodeBit(_choice, 1) == 0)
                    return kNumLowLenSymbols + DecodeBitTree(rc, _mid, (int)(posState << kNumMidLenBits), kNumMidLenBits);
                return kNumLowLenSymbols + kNumMidLenSymbols + DecodeBitTree(rc, _high, 0, kNumHighLenBits);
            }
        }

        private sealed class RangeCoder
        {
            private readonly byte[] _buf;
            private int _pos;
            private uint _range = 0xFFFFFFFFu;
            private uint _code;

            public RangeCoder(byte[] buf, int offset)
            {
                _buf = buf;
                _pos = offset;
                for (int i = 0; i < 5; i++)
                    _code = (_code << 8) | ReadByte();
            }

            private byte ReadByte()
            {
                return _pos < _buf.Length ? _buf[_pos++] : (byte)0;
            }

            public uint DecodeDirectBits(int numTotalBits)
            {
                uint result = 0;
                for (int i = numTotalBits; i > 0; i--)
                {
                    _range >>= 1;
                    uint t = (_code - _range) >> 31;
                    _code -= _range & (t - 1);
                    result = (result << 1) | (1 - t);
                    if (_range < kTopValue)
                    {
                        _code = (_code << 8) | ReadByte();
                        _range <<= 8;
                    }
                }
                return result;
            }

            public uint DecodeBit(ushort[] probs, int index)
            {
                uint prob = probs[index];
                uint newBound = (_range >> kNumBitModelTotalBits) * prob;
                if (_code < newBound)
                {
                    _range = newBound;
                    probs[index] = (ushort)(prob + ((kBitModelTotal - prob) >> kNumMoveBits));
                    if (_range < kTopValue)
                    {
                        _code = (_code << 8) | ReadByte();
                        _range <<= 8;
                    }
                    return 0;
                }
                else
                {
                    _range -= newBound;
                    _code -= newBound;
                    probs[index] = (ushort)(prob - (prob >> kNumMoveBits));
                    if (_range < kTopValue)
                    {
                        _code = (_code << 8) | ReadByte();
                        _range <<= 8;
                    }
                    return 1;
                }
            }
        }
    }
}
