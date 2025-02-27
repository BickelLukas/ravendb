using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Sparrow;
using Sparrow.Server;
using Sparrow.Server.Utils.VxSort;
using Sparrow.Threading;
using Voron.Impl;
using Voron.Util;
using Voron.Util.PFor;
using Constants = Voron.Global.Constants;

namespace Voron.Data.PostingLists;

public readonly unsafe struct PostingListLeafPage
{
    public const int MinimumSizeOfBuffer = 256;

    public static int GetNextValidBufferSize(int size)
    {
        return (size + 255) / 256 * 256;
    }

    private readonly Page _page;
    public PostingListLeafPageHeader* Header => (PostingListLeafPageHeader*)_page.Pointer;

    public int SpaceUsed => Header->SizeUsed;

    public PostingListLeafPage(Page page)
    {
        _page = page;
    }

    public static void InitLeaf(PostingListLeafPageHeader* header)
    {
        header->Flags = PageFlags.Single | PageFlags.Other;
        header->PostingListFlags = ExtendedPageType.PostingListLeaf;
        header->SizeUsed = 0;
        header->NumberOfEntries = 0;
    }
    
    /// <summary>
    /// Additions and removals are *sorted* by the caller
    /// maxValidValue is the limit for the *next* page, so we won't consume entries from there
    /// </summary>
    public void Update(LowLevelTransaction tx, FastPForEncoder encoder, ref ContextBoundNativeList<long> tempList, ref long* additions, ref int additionsCount,
        ref long* removals, ref int removalsCount, long maxValidValue)
    {
        var maxAdditionsLimit = new Span<long>(additions, additionsCount).BinarySearch(maxValidValue);
        if (maxAdditionsLimit < 0)
            maxAdditionsLimit = ~maxAdditionsLimit;
        
        var maxRemovalsLimit = new Span<long>(removals, removalsCount).BinarySearch(maxValidValue);
        if (maxRemovalsLimit < 0)
            maxRemovalsLimit = ~maxRemovalsLimit;
      
        if (maxRemovalsLimit == 0 && maxAdditionsLimit == 0)
            return; // nothing to do
        
        tempList.Clear();

        if (Header->NumberOfEntries == 0)
        {
            encoder.Encode(additions, maxAdditionsLimit);
            
            var written = AppendToNewPage(tx, encoder);
            removals += maxRemovalsLimit;
            removalsCount -= maxRemovalsLimit;
            additions += written;
            additionsCount -= written;
            return;
        }

        var tmpPageScope = tx.Allocator.Allocate(Constants.Storage.PageSize, out ByteString tmp);
        var newPagePtr = tmp.Ptr;
            
        PostingListLeafPageHeader* newHeader = (PostingListLeafPageHeader*)newPagePtr;
        Memory.Copy(newPagePtr, Header, PageHeader.SizeOf);
        InitLeaf(newHeader);

        int numberOfEntriesToReserve = Header->NumberOfEntries + maxAdditionsLimit + maxRemovalsLimit;
        int requiredSizeAligned256 = (numberOfEntriesToReserve+255)/256 * 256;
        tempList.EnsureCapacityFor(requiredSizeAligned256);

        tempList.Count = ReadAllEntries(tx.Allocator, tempList.RawItems, tempList.Capacity);
        Debug.Assert(tempList.Count == Header->NumberOfEntries);
        Debug.Assert(tempList.Capacity - tempList.Count >= maxAdditionsLimit + maxRemovalsLimit);
        
        // Merging between existing, additions and removals, there is one scenario where we can just concat the lists together
        // if we have no removals and all of the new additions are *after* the existing ones. Since everything is sorted, this is
        // a very cheap check.
        // existing: [ 10 .. 20 ], removals: [], additions: [ 30 .. 40 ], so result should be [ 10 .. 40 ]
        // In all other scenarios, we have to sort and remove duplicates & removals
        var needSorting = removalsCount > 0 || // any removal force sorting
                          // here we test if the first new addition is smaller than the largest existing, requiring sorting  
                          (maxAdditionsLimit > 0 && additions[0] <= tempList[^1]);

        tempList.AddRangeUnsafe(additions, maxAdditionsLimit);
        tempList.AddRangeUnsafe(removals, maxRemovalsLimit);

        if (needSorting)
        {
            PostingList.SortEntriesAndRemoveDuplicatesAndRemovals(ref tempList);
        }

        int entriesCount = 0;
        int sizeUsed = 0;
        
        if (tempList.Count > 0)
        {
            encoder.Encode(tempList.RawItems, tempList.Count);
            (entriesCount, sizeUsed) = encoder.Write(newPagePtr + PageHeader.SizeOf, Constants.Storage.PageSize - PageHeader.SizeOf);
            Debug.Assert(entriesCount > 0);
        }

        Debug.Assert(sizeUsed <= Constants.Storage.PageSize - PageHeader.SizeOf);
        newHeader->SizeUsed = (ushort)sizeUsed;
        newHeader->NumberOfEntries = entriesCount;
        // clear the parts we aren't using
        Memory.Set(newPagePtr + PageHeader.SizeOf + sizeUsed, 0, Constants.Storage.PageSize - (PageHeader.SizeOf + sizeUsed));
        Debug.Assert(tx.IsDirty(Header->PageNumber));
        Memory.Copy(Header, newPagePtr, Constants.Storage.PageSize);
            
        tmpPageScope.Dispose();
        
        additions += maxAdditionsLimit;
        additionsCount -= maxAdditionsLimit;
        removals += maxRemovalsLimit;
        removalsCount -= maxRemovalsLimit;
    }

    /// <summary>
    /// Additions and removals are *sorted* by the caller
    /// maxValidValue is the limit for the *next* page, so we won't consume entries from there
    /// </summary>
    public int AppendToNewPage(LowLevelTransaction llt, FastPForEncoder encoder)
    {
        Debug.Assert(llt.IsDirty(_page.PageNumber));
        PostingListLeafPageHeader* newHeader = (PostingListLeafPageHeader*)_page.Pointer;
        InitLeaf(newHeader);

        (int entriesCount, int sizeUsed) = encoder.Write(_page.DataPointer, Constants.Storage.PageSize - PageHeader.SizeOf);

        Debug.Assert(entriesCount > 0);
        Debug.Assert(sizeUsed < Constants.Storage.PageSize);
        newHeader->SizeUsed = (ushort)sizeUsed;
        newHeader->NumberOfEntries = entriesCount;
        // clear the parts we aren't using
        Memory.Set(_page.DataPointer+ sizeUsed, 0, Constants.Storage.PageSize - (PageHeader.SizeOf + sizeUsed));

        return entriesCount;
    }

    public struct Iterator
    {
        private FastPForBufferedReader _reader;
        
        public void Init(byte* start, int sizeUsed) => _reader.Init(start, sizeUsed);

        public Iterator(ByteStringContext allocator)
        {
            _reader = new FastPForBufferedReader(allocator);
        }

        public int Fill(Span<long> matches, out bool hasPrunedResults, long pruneGreaterThanOptimization)
        {
            int totalRead = 0;
            hasPrunedResults = false;
            fixed (long* m = matches)
            {
                while (totalRead < matches.Length)
                {
                    var r = _reader.Fill(m + totalRead, matches.Length - totalRead);
                    if (r == 0)
                        break;

                    totalRead += r;
                    
                    if (m[totalRead - 1] >= pruneGreaterThanOptimization)
                    {
                        hasPrunedResults = true;
                        break;
                    }
                }

                return totalRead;
            }
        }
        

        /// <summary>
        ///  This will find the *range* in which there are values
        ///  that are greater or equal to from, not the exact match
        /// </summary>
        public bool SkipHint(long from)
        {
            if (from == long.MinValue)
                return true; // nothing to do here
            
            var buffer = stackalloc long[MinimumSizeOfBuffer];
            // we *copy* the reader to do the search without modifying 
            //  the original position
            var innerReader = _reader.Decoder;
            while (true)
            {
                var read = innerReader.Read(buffer, MinimumSizeOfBuffer);
                if (read == 0)
                {
                    return false; // not found
                }

                if (from > buffer[read - 1]) 
                    continue;
                
                return true;
            }
        }
    }

    private int ReadAllEntries(ByteStringContext context, long* existing, int capacity)
    {
        int existingCount = 0;
        var reader = new FastPForBufferedReader(context, _page.DataPointer, Header->SizeUsed);

        while (true) 
        {
            var read = reader.Fill(existing + existingCount, capacity - existingCount);
            existingCount += read;
            if (read == 0)
            {
                reader.Dispose();
                return existingCount;
            }
        }
    }

    public List<long> GetDebugOutput()
    {
        using var bsc = new ByteStringContext(SharedMultipleUseFlag.None);
        var buf = new long[(Header->NumberOfEntries + 255)/256 * 256];
        fixed (long* f = buf)
        {
            ReadAllEntries(bsc, f, buf.Length);
        }
        return buf.Take(Header->NumberOfEntries).ToList();
    }

    public void SetIterator(ref Iterator it)
    {
        it.Init(_page.DataPointer, Header->SizeUsed);
    }

    public static bool TryMerge(
        LowLevelTransaction llt,
        ByteStringContext allocator, ref FastPForDecoder fastPForDecoder,
        PostingListLeafPageHeader* dest, PostingListLeafPageHeader* first, PostingListLeafPageHeader* second)
    {
        Debug.Assert(llt.IsDirty(dest->PageNumber));
        Debug.Assert(llt.IsDirty(first->PageNumber));
        Debug.Assert(llt.IsDirty(second->PageNumber));
        var scope = allocator.Allocate(Constants.Storage.PageSize, out ByteString tmp);
        var tmpPtr = tmp.Ptr;
        var newHeader = (PostingListLeafPageHeader*)tmpPtr;
        newHeader->PageNumber = dest->PageNumber;
        InitLeaf(newHeader);

        // using +256 here to ensure that we always have at least 256 available in the buffer
        var mergedList = new ContextBoundNativeList<long>(allocator, first->NumberOfEntries + second->NumberOfEntries + 256);

        if (first->SizeUsed > 0)
        {
            fastPForDecoder.Init((byte*)first + PageHeader.SizeOf, first->SizeUsed);
            mergedList.Count += fastPForDecoder.Read(mergedList.RawItems, mergedList.Capacity);
        }

        if (second->SizeUsed > 0)
        {
            fastPForDecoder.Init((byte*)second + PageHeader.SizeOf, second->SizeUsed);
            mergedList.Count += fastPForDecoder.Read(mergedList.RawItems + first->NumberOfEntries, mergedList.Capacity - first->NumberOfEntries);
        }

        var encoder = new FastPForEncoder(allocator);
        int reqSize = 0;
        if (mergedList.Count > 0)
        {
            reqSize = encoder.Encode(mergedList.RawItems, mergedList.Count);
            if (reqSize >= Constants.Storage.PageSize - PageHeader.SizeOf)
            {
                scope.Dispose();
                return false;
            }
            
            encoder.Write(tmpPtr + PageHeader.SizeOf, Constants.Storage.PageSize - PageHeader.SizeOf);
        }
        mergedList.Dispose();

        newHeader->SizeUsed = (ushort)reqSize;
        newHeader->NumberOfEntries =  mergedList.Count;

        Memory.Set((byte*)dest + PageHeader.SizeOf + dest->SizeUsed, 0,
            Constants.Storage.PageSize - (PageHeader.SizeOf + dest->SizeUsed));
        
        tmp.CopyTo((byte*)dest);
        
        scope.Dispose();
        
        return true;
    }
}
