using Lexis.API.Data;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Lexis.Core.Services
{
    public class DocumentChunkRepository : IDocumentChunkRepository
    {
        private readonly ApplicationDbContext _context;

        public DocumentChunkRepository(ApplicationDbContext context)
        {
            _context = context;
        }


        public async Task AddRangeAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken)
        {
            if (chunks == null)
                throw new ArgumentNullException(nameof(chunks));

            var chunkList = chunks.ToList();

            if (chunkList.Count == 0)
                return;

            await _context.DocumentChunks.AddRangeAsync(chunkList, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
        }


        public async Task<List<DocumentChunk>> GetByIdsAsync(IEnumerable<Guid> chunkIds, CancellationToken cancellationToken)
        {
            var idList = chunkIds.ToList();

            if (idList.Count == 0)
                return [];

            var chunks = await _context.DocumentChunks
                .Where(c => idList.Contains(c.Id))
                .ToListAsync(cancellationToken);

            return idList
                .Join(chunks, id => id, chunk => chunk.Id,(_, chunk) => chunk)
                .ToList();
        }


        public async Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken)
        {
            await _context.DocumentChunks
                .Where(c => c.DocumentId == documentId)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
