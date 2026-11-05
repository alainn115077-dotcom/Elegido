using System.Globalization;
using ElegidoManager.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ElegidoManager.Data
{
    /// <summary>Un cambio pendiente de escribir en MongoDB (se captura antes de guardar).</summary>
    public sealed class CambioMongo
    {
        public required IEntityType Tipo { get; init; }
        public required EntityState Estado { get; init; }
        public required object Entidad { get; init; }
        public required int Id { get; init; }
        public List<string> Modificadas { get; init; } = new();
    }

    /// <summary>
    /// Almacén en MongoDB (Atlas). Cada tipo de entidad es una colección con el nombre de la clase
    /// (Usuario, Cliente, Contratacion…); el campo _id es el Id numérico de siempre.
    /// Los PDF de los documentos se guardan aparte en la colección "Archivos" para no cargarlos en cada consulta.
    /// </summary>
    public class MongoAlmacen
    {
        private const string FormatoFecha = "yyyy-MM-dd'T'HH:mm:ss.fffffff";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Tipos que NO se cargan en cada petición (crecen sin límite): se consultan directo.</summary>
        public static readonly HashSet<Type> SinCargar = new() { typeof(Bitacora) };

        private readonly IMongoDatabase _db;
        public MongoAlmacen(string uri, string baseDatos)
        {
            var cliente = new MongoClient(uri);
            _db = cliente.GetDatabase(baseDatos);
        }

        private IMongoCollection<BsonDocument> Coleccion(string nombre) => _db.GetCollection<BsonDocument>(nombre);
        private IMongoCollection<BsonDocument> Coleccion(IEntityType t) => Coleccion(t.ClrType.Name);

        // ------------------------------------------------------------------ IDs
        /// <summary>Siguiente Id para una colección (contador atómico, sirve con varias instancias a la vez).</summary>
        public int SiguienteId(string coleccion)
        {
            var r = Coleccion("Contadores").FindOneAndUpdate(
                Builders<BsonDocument>.Filter.Eq("_id", coleccion),
                Builders<BsonDocument>.Update.Inc("valor", 1),
                new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After });
            return r["valor"].ToInt32();
        }

        // ------------------------------------------------------------------ Arranque
        /// <summary>Ajusta contadores, crea índices y agrega los integrantes la primera vez.</summary>
        public void Inicializar(IModel modelo)
        {
            foreach (var t in modelo.GetEntityTypes().Where(t => !t.IsOwned()))
            {
                var col = Coleccion(t);
                var ultimo = col.Find(FilterDefinition<BsonDocument>.Empty)
                    .Sort(Builders<BsonDocument>.Sort.Descending("_id")).Limit(1).FirstOrDefault();
                var max = ultimo == null ? 0 : ultimo["_id"].ToInt32();
                Coleccion("Contadores").UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("_id", t.ClrType.Name),
                    Builders<BsonDocument>.Update.Max("valor", max),
                    new UpdateOptions { IsUpsert = true });
            }

            Coleccion(nameof(Bitacora)).Indexes.CreateOne(
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Descending("Fecha")));
        }

        // ------------------------------------------------------------------ Lectura
        /// <summary>Carga todos los documentos de un tipo de entidad como objetos .NET.</summary>
        public List<object> LeerTodos(IEntityType tipo) => LeerTodosAsync(tipo).GetAwaiter().GetResult();

        /// <summary>Lee varias colecciones a la vez (en paralelo) para que cada página cargue rápido.</summary>
        public List<object> LeerVarios(IEnumerable<IEntityType> tipos)
        {
            var tareas = tipos.Select(LeerTodosAsync).ToList();
            Task.WhenAll(tareas).GetAwaiter().GetResult();
            return tareas.SelectMany(t => t.Result).ToList();
        }

        public async Task<List<object>> LeerTodosAsync(IEntityType tipo)
        {
            var props = tipo.GetProperties().ToList();
            var lista = new List<object>();
            var docs = await Coleccion(tipo).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync().ConfigureAwait(false);
            foreach (var doc in docs)
            {
                var entidad = Activator.CreateInstance(tipo.ClrType)!;
                foreach (var p in props)
                {
                    if (p.PropertyInfo == null) continue;
                    var nombre = p.IsPrimaryKey() ? "_id" : p.Name;
                    if (!doc.TryGetValue(nombre, out var valor)) continue;
                    p.PropertyInfo.SetValue(entidad, DeBson(valor, p.ClrType));
                }
                lista.Add(entidad);
            }
            return lista;
        }

        public byte[] LeerArchivo(int documentoId)
        {
            var doc = Coleccion("Archivos").Find(Builders<BsonDocument>.Filter.Eq("_id", documentoId)).FirstOrDefault();
            return doc != null && doc.TryGetValue("Contenido", out var c) && c.IsBsonBinaryData ? c.AsByteArray : Array.Empty<byte>();
        }

        public async Task<List<Bitacora>> LeerBitacoraAsync(int limite)
        {
            var docs = await Coleccion(nameof(Bitacora)).Find(FilterDefinition<BsonDocument>.Empty)
                .Sort(Builders<BsonDocument>.Sort.Descending("_id")).Limit(limite).ToListAsync();
            return docs.Select(d => new Bitacora
            {
                Id = d["_id"].ToInt32(),
                Fecha = (DateTime)DeBson(d.GetValue("Fecha", BsonNull.Value), typeof(DateTime))!,
                Usuario = d.GetValue("Usuario", "").ToString()!,
                Accion = d.GetValue("Accion", "").ToString()!,
                Detalle = d.GetValue("Detalle", BsonNull.Value).IsBsonNull ? null : d["Detalle"].ToString(),
                Ip = d.GetValue("Ip", BsonNull.Value).IsBsonNull ? null : d["Ip"].ToString()
            }).ToList();
        }

        // ------------------------------------------------------------------ Escritura
        public static List<CambioMongo> Capturar(ChangeTracker tracker)
        {
            var lista = new List<CambioMongo>();
            foreach (var e in tracker.Entries())
            {
                if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
                var clave = e.Metadata.FindPrimaryKey()!.Properties[0];
                var id = Convert.ToInt32(e.Property(clave.Name).CurrentValue, Inv);
                lista.Add(new CambioMongo
                {
                    Tipo = e.Metadata,
                    Estado = e.State,
                    Entidad = e.Entity,
                    Id = id,
                    Modificadas = e.State == EntityState.Modified
                        ? e.Properties.Where(p => p.IsModified && !p.Metadata.IsPrimaryKey()).Select(p => p.Metadata.Name).ToList()
                        : new List<string>()
                });
            }
            return lista;
        }

        public void Escribir(IEnumerable<CambioMongo> cambios) => EscribirAsync(cambios).GetAwaiter().GetResult();

        public async Task EscribirAsync(IEnumerable<CambioMongo> cambios, CancellationToken ct = default)
        {
            foreach (var grupo in cambios.GroupBy(c => c.Tipo))
            {
                var tipo = grupo.Key;
                var col = Coleccion(tipo);
                var esDocumento = tipo.ClrType == typeof(Documento);
                var ops = new List<WriteModel<BsonDocument>>();
                var archivos = new List<WriteModel<BsonDocument>>();

                foreach (var c in grupo)
                {
                    var filtro = Builders<BsonDocument>.Filter.Eq("_id", c.Id);
                    switch (c.Estado)
                    {
                        case EntityState.Added:
                            ops.Add(new ReplaceOneModel<BsonDocument>(filtro, ABsonDocumento(tipo, c.Entidad)) { IsUpsert = true });
                            if (esDocumento && c.Entidad is Documento d && d.Contenido.Length > 0)
                                archivos.Add(new ReplaceOneModel<BsonDocument>(filtro,
                                    new BsonDocument { { "_id", c.Id }, { "Contenido", new BsonBinaryData(d.Contenido) } }) { IsUpsert = true });
                            break;

                        case EntityState.Modified:
                            var campos = c.Modificadas.Where(n => !(esDocumento && n == nameof(Documento.Contenido))).ToList();
                            if (campos.Count == 0) break;
                            var set = new BsonDocument();
                            foreach (var n in campos)
                                set[n] = ABson(tipo.FindProperty(n)!.PropertyInfo?.GetValue(c.Entidad));
                            ops.Add(new UpdateOneModel<BsonDocument>(filtro, new BsonDocument("$set", set)) { IsUpsert = true });
                            break;

                        case EntityState.Deleted:
                            ops.Add(new DeleteOneModel<BsonDocument>(filtro));
                            if (esDocumento) archivos.Add(new DeleteOneModel<BsonDocument>(filtro));
                            break;
                    }
                }
                if (ops.Count > 0) await col.BulkWriteAsync(ops, cancellationToken: ct);
                if (archivos.Count > 0) await Coleccion("Archivos").BulkWriteAsync(archivos, cancellationToken: ct);
            }
        }

        private static BsonDocument ABsonDocumento(IEntityType tipo, object entidad)
        {
            var doc = new BsonDocument();
            foreach (var p in tipo.GetProperties())
            {
                if (p.PropertyInfo == null) continue;
                if (tipo.ClrType == typeof(Documento) && p.Name == nameof(Documento.Contenido)) continue;
                doc[p.IsPrimaryKey() ? "_id" : p.Name] = ABson(p.PropertyInfo.GetValue(entidad));
            }
            return doc;
        }

        // ------------------------------------------------------------------ Conversión de valores
        public static BsonValue ABson(object? v) => v switch
        {
            null => BsonNull.Value,
            string s => new BsonString(s),
            int i => new BsonInt32(i),
            long l => new BsonInt64(l),
            bool b => BsonBoolean.Create(b),
            decimal m => new BsonDecimal128(new Decimal128(m)),
            double d => new BsonDouble(d),
            DateTime f => new BsonString(f.ToString(FormatoFecha, Inv)),
            TimeSpan ts => new BsonString(ts.ToString("c", Inv)),
            Guid g => new BsonString(g.ToString()),
            byte[] bytes => new BsonBinaryData(bytes),
            Enum e => new BsonString(e.ToString()),
            _ => new BsonString(Convert.ToString(v, Inv) ?? "")
        };

        public static object? DeBson(BsonValue b, Type tipo)
        {
            var t = Nullable.GetUnderlyingType(tipo) ?? tipo;
            if (b.IsBsonNull)
            {
                if (t == typeof(string) || !tipo.IsValueType || Nullable.GetUnderlyingType(tipo) != null) return t == typeof(byte[]) ? Array.Empty<byte>() : null;
                return Activator.CreateInstance(t);
            }
            if (t == typeof(string)) return b.IsString ? b.AsString : b.ToString();
            if (t == typeof(int)) return b.ToInt32();
            if (t == typeof(long)) return b.ToInt64();
            if (t == typeof(bool)) return b.ToBoolean();
            if (t == typeof(decimal)) return b.IsDecimal128 ? Decimal128.ToDecimal(b.AsDecimal128) : Convert.ToDecimal(b.ToDouble(), Inv);
            if (t == typeof(double)) return b.ToDouble();
            if (t == typeof(DateTime))
                return b.IsBsonDateTime ? b.ToUniversalTime().ToLocalTime()
                    : DateTime.ParseExact(b.AsString, FormatoFecha, Inv, DateTimeStyles.None);
            if (t == typeof(TimeSpan)) return TimeSpan.Parse(b.AsString, Inv);
            if (t == typeof(Guid)) return Guid.Parse(b.AsString);
            if (t == typeof(byte[])) return b.IsBsonBinaryData ? b.AsByteArray : Array.Empty<byte>();
            if (t.IsEnum) return Enum.Parse(t, b.AsString);
            return Convert.ChangeType(b.ToString(), t, Inv);
        }
    }
}
