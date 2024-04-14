//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System.Collections.Generic;

namespace DiscUtils.Net.Dns;

internal class Message
{
    public Message()
    {
        Questions = [];
        Answers = [];
        AuthorityRecords = [];
        AdditionalRecords = [];
    }

    public List<ResourceRecord> AdditionalRecords { get; }

    public List<ResourceRecord> Answers { get; }

    public List<ResourceRecord> AuthorityRecords { get; }

    public MessageFlags Flags { get; set; }

    public List<Question> Questions { get; }

    public ushort TransactionId { get; set; }

    public static Message Read(PacketReader reader)
    {
        var result = new Message
        {
            TransactionId = reader.ReadUShort(),
            Flags = new MessageFlags(reader.ReadUShort())
        };

        var questions = reader.ReadUShort();
        var answers = reader.ReadUShort();
        var authorityRecords = reader.ReadUShort();
        var additionalRecords = reader.ReadUShort();

        for (var i = 0; i < questions; ++i)
        {
            result.Questions.Add(Question.ReadFrom(reader));
        }

        for (var i = 0; i < answers; ++i)
        {
            result.Answers.Add(ResourceRecord.ReadFrom(reader));
        }

        for (var i = 0; i < authorityRecords; ++i)
        {
            result.AuthorityRecords.Add(ResourceRecord.ReadFrom(reader));
        }

        for (var i = 0; i < additionalRecords; ++i)
        {
            result.AdditionalRecords.Add(ResourceRecord.ReadFrom(reader));
        }

        return result;
    }

    public void WriteTo(PacketWriter writer)
    {
        writer.Write(TransactionId);
        writer.Write(Flags.Value);
        writer.Write((ushort)Questions.Count);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        foreach (var question in Questions)
        {
            question.WriteTo(writer);
        }
    }
}