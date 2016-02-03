﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using Xbim.Common;
using Xbim.Common.Exceptions;
using Xbim.Common.Metadata;

namespace Xbim.IO.TableStore
{
    public partial class TableStore
    {
        public TextWriter Log { get; private set; }

        #region Reading in from a spreadsheet
        public void LoadFrom(string path)
        {
            if (path == null)
                throw new ArgumentNullException("path");

            Log = new StringWriter();

            var ext = Path.GetExtension(path).ToLower().Trim('.');
            if (ext != "xls" && ext != "xlsx")
            {
                //XLSX is Spreadsheet XML representation which is capable of storing more data
                path += ".xlsx";
                ext = "xlsx";
            }
            using (var file = File.OpenRead(path))
            {
                var type = ext == "xlsx" ? ExcelTypeEnum.XLSX : ExcelTypeEnum.XLS;
                LoadFrom(file, type);
                file.Close();
            }


        }

        public void LoadFrom(Stream stream, ExcelTypeEnum type)
        {
            IWorkbook workbook;
            switch (type)
            {
                case ExcelTypeEnum.XLS:
                    workbook = new HSSFWorkbook(stream);
                    break;
                case ExcelTypeEnum.XLSX: //this is as it should be according to a standard
                    workbook = new XSSFWorkbook(stream);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("type");
            }

            //refresh cache. This might change in between two loadings
            _multiRowIndicesCache = new Dictionary<string, int[]>();
            _typeHintCache = new Dictionary<ClassMapping, Dictionary<string, int>>();
            _isMultiRowMappingCache = new Dictionary<ClassMapping, bool>();
            _referenceContexts = new Dictionary<ClassMapping, ReferenceContext>();
            _forwardReferences.Clear();

            //create spreadsheet representaion 
            LoadFromWoorkbook(workbook);
        }

        private void LoadFromWoorkbook(IWorkbook workbook)
        {
            //get all data tables
            if (Mapping.ClassMappings == null || !Mapping.ClassMappings.Any())
                return;

            var sheetsNumber = workbook.NumberOfSheets;
            for (var i = 0; i < sheetsNumber; i++)
            {
                var sheetName = workbook.GetSheetName(i);
                var mapping =
                    Mapping.ClassMappings.FirstOrDefault(
                        m => string.Equals(sheetName, m.TableName, StringComparison.OrdinalIgnoreCase));
                if (mapping == null)
                    continue;
                var sheet = workbook.GetSheet(sheetName);

                if (mapping.IsPartial)
                    continue;
                LoadFromSheet(sheet, mapping);
            }

            //reconstruct references (possibly forward references)
            foreach (var reference in _forwardReferences)
                reference.Resolve(Log);
            _forwardReferences.Clear();

            //load partial tables (also just a references)

            //be happy
        }

        private void LoadFromSheet(ISheet sheet, ClassMapping mapping)
        {
            //if there is only header in a sheet, don't waste resources
            if (sheet.LastRowNum < 1)
                return;

            //adjust mapping to sheet in case columns are in a different order
            AdjustMapping(sheet, mapping);
            CacheColumnIndices(mapping);

            //cache key columns
            CacheMultiRowIndices(mapping);

            //cache contexts
            CacheReferenceContexts(mapping);

            //iterate over rows (be careful about MultiRow != None, merge values if necessary)
            var enumerator = sheet.GetRowEnumerator();
            IRow lastRow = null;
            IPersistEntity lastEntity = null;
            var emptyCells = 0;
            while (enumerator.MoveNext())
            {
                var row = enumerator.Current as IRow;
                //skip header row
                if (row == null || row.RowNum == 0)
                    continue;

                if (!row.Cells.Any())
                {
                    emptyCells++;
                    if (emptyCells == 3)
                        //break processing if this is third empty row
                        break;
                    //skip empty row
                    continue;
                }
                emptyCells = 0;

                //last row might be used in case this is a MultiRow
                lastEntity = LoadFromRow(row, mapping, lastRow, lastEntity);
                lastRow = row;
            }
        }

        private void CacheReferenceContexts(ClassMapping mapping)
        {
            if (_referenceContexts.ContainsKey(mapping))
                return;
            var context = new ReferenceContext(this, mapping);
            _referenceContexts.Add(mapping, context);
        }

        /// <summary>
        /// All indices should be cached already
        /// </summary>
        /// <param name="mapping"></param>
        /// <returns></returns>
        private int[] GetIdentityIndices(ClassMapping mapping)
        {
            return _multiRowIndicesCache[mapping.TableName];
        }

        private void CacheMultiRowIndices(ClassMapping mapping)
        {
            int[] existing;
            //one table might be defined for multiple classes but it has to have the same structure and constrains
            _multiRowIndicesCache.TryGetValue(mapping.TableName, out existing);

            var indices = new int[0];
            if (mapping.PropertyMappings != null && mapping.PropertyMappings.Any())
                indices = mapping.PropertyMappings
                    .Where(p => p.IsMultiRowIdentity)
                    .Select(m => m.ColumnIndex)
                    .ToArray();
           
            if (existing != null)
            {
                //update and check if it is consistent. Report inconsistency.
                _multiRowIndicesCache[mapping.TableName] = indices;
                if (existing.Length != indices.Length || !existing.SequenceEqual(indices))
                    Log.WriteLine("Table {0} is defined in multiple class mappings with different key columns for a multi-value records", mapping.TableName);
            }
            else
                _multiRowIndicesCache.Add(mapping.TableName, indices);

        }

        private IPersistEntity LoadFromRow(IRow row, ClassMapping mapping, IRow lastRow, IPersistEntity lastEntity)
        {
            var multirow = IsMultiRow(row, mapping, lastRow);

            if (multirow)
            {
                //only add multivalue to the multivalue properties of last entity
                var multiProps = mapping.PropertyMappings.Where(m => m.MultiRow != MultiRow.None);
                var lastExpType = lastEntity.ExpressType;
                foreach (var prop in multiProps)
                {
                    SetPropertyValue(lastEntity, lastExpType, prop, row, mapping);
                }
                //throw new NotImplementedException();
                return lastEntity;
            }

            //get type of the coresponding object from ClassMapping or from a type hint, create instance
            var entity = GetNewEntity(row, mapping);
            var eType = entity.ExpressType;
            foreach (var prop in mapping.PropertyMappings.Where(m => m.Status != DataStatus.Reference))
            {
                //fill in simple value fields
                SetPropertyValue(entity, eType, prop, row, mapping);
            }

            if (mapping.PropertyMappings.Any(m => m.Status != DataStatus.Reference))
            {
                _forwardReferences.Add(new ForwardReference(entity, row, mapping, this));
            }
            return entity;
        }

        //Express Type could be obtained inside but it would involve unnecessary number of look-ups internally
        private void SetPropertyValue(IPersistEntity entity, ExpressType eType, PropertyMapping pMapping, IRow row, ClassMapping cMapping)
        {
            var colIdx = pMapping.ColumnIndex;
            var cell = row.GetCell(colIdx);
            //if it is not defined or it is just a default value, do nothing
            if (cell == null || (cell.CellType == CellType.String && string.Equals(cell.StringCellValue, pMapping.DefaultValue, StringComparison.OrdinalIgnoreCase)))
                return;

            //only first of possible search paths is used to set the value
            var path = pMapping.Paths.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(path))
                return;

            //We have created this object already (entity). If this cell contains any useful information
            //it had been used before.
            if (string.Equals(path, "[type]", StringComparison.Ordinal))
                return;

            if (pMapping.Status == DataStatus.Reference)
                //forward reference was created already elsewhere
                return;

            SetPropertyValue(entity, eType, path, cell, cMapping, pMapping);
        }

        private void SetPropertyValue(IPersistEntity entity, ExpressType eType, string path, ICell cell, ClassMapping cMapping, PropertyMapping pMapping)
        {
            //process the path (simple value, local reference, global reference)
            var parts = path.Split('.');
            for (var ip = 0; ip < parts.Length; ip++)
            {
                var propName = parts[ip];
                var index = GetPropertyIndex(ref propName);
                var pInfo = GetPropertyInfo(propName, eType, index);

                var pType = pInfo.PropertyType;
                //if it is an abstract type or interface we need to find a concrete type
                if (pType.IsAbstract)
                {
                    pType = GetConcreteType(pType, cMapping, cell, pMapping);
                    if (pType == null)
                    {
                        Log.WriteLine("It wasn't possible to find a concrete type for row {0}, cell {1}, table {2}", cell.Row.RowNum, cell.ColumnIndex, cMapping.TableName);
                        break;
                    }
                }

                //simple value
                if (pType.IsValueType || typeof(string) == pType)
                {
                    if (pInfo.GetSetMethod() == null)
                    {
                        if (pInfo.DeclaringType != null)
                            Log.WriteLine("There is no setter for {0} in {1}", pInfo.Name, pInfo.DeclaringType.Name);
                        break;
                    }
                    SetSimpleValue(pInfo, entity, cell, index);
                    break;
                }

                //simple value enumeration
                var setType = GetSimpleValueEnumType(pType);
                if (setType != null)
                {
                    var set = pInfo.GetValue(entity, null) as IList;
                    if (set != null && cell.CellType == CellType.String)
                    {
                        var strVal = cell.StringCellValue;
                        var strParts = strVal.Split(new[] { Mapping.ListSeparator },
                            StringSplitOptions.RemoveEmptyEntries)
                            .Select(i => i.Trim());
                        foreach (var strPart in strParts)
                        {
                            var val = CreateSimpleValue(setType, strPart);
                            if (val != null)
                                set.Add(val);
                        }
                        break;
                    }
                }

                if (!pType.IsAbstract && typeof(IPersistEntity).IsAssignableFrom(pType))
                {
                    if (IsGlobalType(pType))
                    {
                        //todo: get or create after all entities are read (so it can use created entities by key values)
                    }
                    else
                    {
                        //get (if it was created before for other path) or create
                        var child = pInfo.GetValue(entity, index == null ? null : new[] { index }) as IPersistEntity;
                        if (child == null)
                        {
                            child = Model.Instances.New(pType);
                            pInfo.SetValue(entity, child, index == null ? null : new[] { index });
                        }
                            

                        //get sub-path
                        var subParts = parts.ToList().GetRange(ip + 1, parts.Length - ip - 1);
                        var subPath = string.Join(".", subParts);

                        //recursive set values
                        SetPropertyValue(child, child.ExpressType, subPath, cell, cMapping, pMapping);
                    }
                    break;
                }

                var eProperty = GetProperty(eType, propName);
                
                //enumerable of IPersistEntity or IEspressSelect
                if (eProperty.EnumerableType != null)
                {
                    if (eProperty.IsInverse)
                    {
                        Log.WriteLine("Inverse property {0} of {1} is used in a way where it is not possible to load it into memory.", propName, eType.ExpressName);
                        break;
                    }
                    if (eProperty.IsDerived)
                    {
                        Log.WriteLine("Derived property {0} of {1} is used in a way where it is not possible to load it into memory.", propName, eType.ExpressName);
                        break;
                    }
                    Log.WriteLine("Property {0} of {1} is used in a way where it is not possible to load it into memory. It should probably be marked as a reference value.", propName, eType.ExpressName);
                    break;
                }
            }
        }


        private Type GetConcreteType(Type absType, ClassMapping cMapping, ICell cell, PropertyMapping pMapping)
        {
            //if it is not an abstract type just return it
            if (!absType.IsAbstract)
                return absType;

            int hintColumn;
            Dictionary<string, int> hintPaths;
            if (!_typeHintCache.TryGetValue(cMapping, out hintPaths))
            {
                hintPaths = new Dictionary<string, int>();
                _typeHintCache.Add(cMapping, hintPaths);
            }
            var path = pMapping.Paths.FirstOrDefault() ?? "";
            if (!hintPaths.TryGetValue(path, out hintColumn))
            {
                //try to find a hint
                var pattern = path + "[type]";
                var hintMapping = cMapping.PropertyMappings
                    .FirstOrDefault(m => m.Paths.Any(p => p.EndsWith(pattern)));
                hintColumn = hintMapping != null
                    ? hintMapping.ColumnIndex
                    : -1;
                hintPaths.Add(path, hintColumn);
            }

            //try to get a hint type from a cell
            if (hintColumn >= 0)
            {
                var hintCell = cell.Row.GetCell(hintColumn);
                if (hintCell != null && hintCell.CellType == CellType.String)
                {
                    var hintType = hintCell.StringCellValue;
                    if (!string.IsNullOrWhiteSpace(hintType))
                    {
                        var result = MetaData.ExpressType(hintType.ToUpper());
                        if (result != null && !result.Type.IsAbstract)
                            return result.Type;
                    }
                }
            }

            //use resolver if available
            if (Resolvers != null)
            {
                var resolver = Resolvers.FirstOrDefault(r => r.CanResolve(absType));
                if (resolver != null)
                    return resolver.Resolve(absType, cell, cMapping, pMapping);
            }

            //return null if no non-abstract type could have been found
            return null;
        }

        private static Type GetSimpleValueEnumType(Type type)
        {
            if (type.IsValueType || type== typeof(string))
                return null;

            if (!type.IsGenericType)
                return null;

            type = type.GetGenericArguments()[0];
            return type.IsValueType || type == typeof (string)
                ? type
                : null;
        }

        private bool IsGlobalType(Type type)
        {
            var gt = _globalTypes ??
                     (_globalTypes =
                         Mapping.Scopes.Where(s => s.Scope == ClassScopeEnum.Model)
                             .Select(s => MetaData.ExpressType(s.Class.ToUpper()))
                             .ToList());
            return gt.Any(t => t.Type == type || t.SubTypes.Any(st => st.Type == type));
        }


        private bool IsMultiRow(IRow row, ClassMapping mapping, IRow lastRow)
        {
            if (lastRow == null) return false;

            bool isMultiMapping;
            if (_isMultiRowMappingCache.TryGetValue(mapping, out isMultiMapping))
            {
                if (!isMultiMapping) return false;
            }
            else
            {
                if (mapping.PropertyMappings == null || !mapping.PropertyMappings.Any())
                {
                    _isMultiRowMappingCache.Add(mapping, false);
                    return false;
                }

                var multiRowProperty = mapping.PropertyMappings.FirstOrDefault(m => m.MultiRow != MultiRow.None);
                if (multiRowProperty == null)
                {
                    _isMultiRowMappingCache.Add(mapping, false);
                    return false;
                }

                _isMultiRowMappingCache.Add(mapping, true);
            }

            
            var keyIndices = GetIdentityIndices(mapping);
            foreach (var index in keyIndices)
            {
                var cellA = row.GetCell(index);
                var cellB = lastRow.GetCell(index);

                if(cellA == null || cellB == null)
                    return false;

                if (cellA.CellType == CellType.Blank || cellB.CellType == CellType.Blank)
                    return false;

                if (cellA.CellType != cellB.CellType)
                    return false;

                switch (cellA.CellType)
                {
                    case CellType.Unknown:
                        break;
                    case CellType.Numeric:
                        if (Math.Abs(cellA.NumericCellValue - cellB.NumericCellValue) > 1e-9)
                            return false;
                        break;
                    case CellType.String:
                        if (cellA.StringCellValue != cellB.StringCellValue)
                            return false;
                        break;
                    case CellType.Formula:
                        break;
                    case CellType.Blank:
                        break;
                    case CellType.Boolean:
                        if (cellA.BooleanCellValue != cellB.BooleanCellValue)
                            return false;
                        break;
                    case CellType.Error:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return true;
        }

        private IPersistEntity GetNewEntity(IRow row, ClassMapping mapping)
        {
            var eType = GetConcreteType(row, mapping);
            return Model.Instances.New(eType.Type);
        }

        private ExpressType GetConcreteType(IRow row, ClassMapping mapping)
        {
            string typeName = null;

            //type hint property has priority
            var hintProperty = mapping.PropertyMappings.FirstOrDefault(m => string.Equals(m.Paths.FirstOrDefault(), "[type]", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(m.Column));

            if (hintProperty != null)
            {
                var hintCell = row.GetCell(hintProperty.ColumnIndex);
                if (hintCell != null && hintCell.CellType == CellType.String)
                    typeName = hintCell.StringCellValue;
            }
            else
                typeName = mapping.Class;

            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            var eType = MetaData.ExpressType(typeName.ToUpper());
            if (eType != null && !eType.Type.IsAbstract)
                return eType;

            //use fallback to retrieve a non-abstract type (defined in a configuration file?)
            var fbTypeName = mapping.FallBackConcreteType;
            if (string.IsNullOrWhiteSpace(fbTypeName))
                throw new XbimException(string.Format("It wasn't possible to find a non-abstract type for table {0}, class {1}", mapping.TableName, mapping.Class));

            eType = MetaData.ExpressType(fbTypeName.ToUpper());
            if (eType != null && !eType.Type.IsAbstract)
                return eType;

            throw new XbimException(string.Format("It wasn't possible to find a non-abstract type for table {0}, class {1}", mapping.TableName, mapping.Class));
        }

        private static void CacheColumnIndices(ClassMapping mapping)
        {
            foreach (var pMap in mapping.PropertyMappings)
                pMap.ColumnIndex = CellReference.ConvertColStringToIndex(pMap.Column);
        }

        private static void AdjustMapping(ISheet sheet, ClassMapping mapping)
        {
            //there is only header
            if (sheet.LastRowNum < 1)
                return;

            //get the header row and analyze it
            var headerRow = sheet.GetRow(0);
            if (headerRow == null)
                return;

            var headings = headerRow.Cells.Where(c => c.CellType == CellType.String || !string.IsNullOrWhiteSpace(c.StringCellValue)).ToList();
            if (!headings.Any())
                return;
            var mappings = mapping.PropertyMappings;
            if (mappings == null || !mappings.Any())
                return;

            foreach (var heading in headings)
            {
                var index = heading.ColumnIndex;
                var column = CellReference.ConvertNumToColString(index).ToUpper();
                var header = heading.StringCellValue;

                var pMapping = mappings.FirstOrDefault(m => string.Equals(m.Header, header, StringComparison.OrdinalIgnoreCase));
                //if no mapping is found things might go wrong or it is just renamed
                if (pMapping == null || string.Equals(pMapping.Column, column, StringComparison.OrdinalIgnoreCase))
                    continue;

                //if the letter is not assigned at all, assign this letter
                if (string.IsNullOrWhiteSpace(pMapping.Column))
                {
                    pMapping.Column = column;
                    continue;
                }

                //move the column mapping to the new position
                var current = mappings.FirstOrDefault(m => string.Equals(m.Column, column, StringComparison.OrdinalIgnoreCase));
                if (current != null)
                    current.Column = null;
                pMapping.Column = column;
            }

            var unassigned = mappings.Where(m => string.IsNullOrWhiteSpace(m.Column)).ToList();
            if (!unassigned.Any())
                return;

            //try to assign letters to the unassigned columns
            foreach (var heading in headings)
            {
                var index = heading.ColumnIndex;
                var column = CellReference.ConvertNumToColString(index).ToUpper();
                var pMapping = mappings.FirstOrDefault(m => string.Equals(m.Column, column, StringComparison.OrdinalIgnoreCase));
                if (pMapping != null)
                    continue;

                var first = unassigned.FirstOrDefault();
                if (first == null) break;

                first.Column = column;
                unassigned.Remove(first);
            }

            //remove unassigned mappings
            if (unassigned.Any())
                return;
            foreach (var propertyMapping in unassigned)
                mapping.PropertyMappings.Remove(propertyMapping);
        }

        private static Type GetNonNullableType(Type type)
        {
            //only value types can be nullable
            if (!type.IsValueType) return type;

            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof (Nullable<>) ? Nullable.GetUnderlyingType(type) : type;
        }

        internal object CreateSimpleValue(Type type, string value)
        {
            var underlying = GetNonNullableType(type);

            var propType = underlying;
            var isExpress = false;

            //dig deeper if it is an express value type
            if (underlying.IsValueType && typeof(IExpressValueType).IsAssignableFrom(underlying))
            {
                var eType = MetaData.ExpressType(underlying);
                if (eType != null)
                {
                    isExpress = true;
                    underlying = GetNonNullableType(eType.UnderlyingType);
                }
            }

            //chack base types
            if (typeof(string) == underlying)
            {
                return isExpress ? Activator.CreateInstance(propType, value) : value;
            }
            if (underlying == typeof (double) || underlying == typeof (float))
            {
                double d;
                if (double.TryParse(value, out d))
                    return isExpress
                    ? Activator.CreateInstance(propType, d)
                    : d;
                return null;
            }
            if (underlying == typeof (int) || underlying == typeof (long))
            {
                var l = type == typeof(int) ? Convert.ToInt32(value) : Convert.ToInt64(value);
                return isExpress ? Activator.CreateInstance(propType, l) : l;
            }
            if (underlying == typeof (DateTime))
            {
                DateTime date;
                return !DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out date) ? 
                    DateTime.Parse("1900-12-31T23:59:59", null, DateTimeStyles.RoundtripKind) : 
                    date;
            }
            if (underlying == typeof (bool))
            {
                bool i;
                if (bool.TryParse(value, out i))
                    return isExpress ? Activator.CreateInstance(propType, i) : i;
                return null;
            }
            if (underlying.IsEnum)
            {
                try
                {
                    var eMember = GetAliasEnumName(underlying, value);
                    //if there was no alias try to parse the value
                    var val = Enum.Parse(underlying, eMember ?? value, true);
                    return val;
                }
                catch (Exception)
                {
                    Log.WriteLine("Enumeration {0} doesn't have a member {1}", underlying.Name, value);
                }
            }
            return null;
        }

        internal object CreateSimpleValue(Type type, ICell cell)
        {
            //return if there is no value in she cell
            if (cell.CellType == CellType.Blank) return null;
            type = GetNonNullableType(type);
            
            var propType = type;
            var isExpress = false;

            //dig deeper if it is an express value type
            if (type.IsValueType && typeof(IExpressValueType).IsAssignableFrom(type))
            {
                var eType = MetaData.ExpressType(type);
                if (eType != null)
                {
                    isExpress = true;
                    type = GetNonNullableType(eType.UnderlyingType);
                }
            }

            if (typeof(string) == type)
            {
                string value = null;
                switch (cell.CellType)
                {
                    case CellType.Numeric:
                        value = cell.NumericCellValue.ToString(CultureInfo.InvariantCulture);
                        break;
                    case CellType.String:
                        value = cell.StringCellValue;
                        break;
                    case CellType.Boolean:
                        value = cell.BooleanCellValue.ToString();
                        break;
                    default:
                        Log.WriteLine("There is no suitable value for {0} in cell {1}{2}, sheet {3}", propType.Name, CellReference.ConvertNumToColString(cell.ColumnIndex), cell.RowIndex + 1, cell.Sheet.SheetName);
                        break;
                }
                return isExpress ? Activator.CreateInstance(propType, value) : value;
            }

            if (type == typeof(DateTime))
            {
                var date = default(DateTime);
                switch (cell.CellType)
                {
                    case CellType.Numeric:
                        date = cell.DateCellValue;
                        break;
                    case CellType.String:
                        if (!DateTime.TryParse(cell.StringCellValue, null, DateTimeStyles.RoundtripKind, out date))
                            //set to default value according to specification
                            date = DateTime.Parse("1900-12-31T23:59:59", null, DateTimeStyles.RoundtripKind);
                        break;
                    default:
                        Log.WriteLine("There is no suitable value for {0} in cell {1}{2}, sheet {3}", propType.Name, CellReference.ConvertNumToColString(cell.ColumnIndex), cell.RowIndex + 1, cell.Sheet.SheetName);
                        break;
                }
                return date;
            }

            if (type == typeof(double) || type == typeof(float))
            {
                switch (cell.CellType)
                {
                    case CellType.Numeric:
                        return isExpress
                            ? Activator.CreateInstance(propType, cell.NumericCellValue)
                            : cell.NumericCellValue;
                    case CellType.String:
                        double d;
                        if (double.TryParse(cell.StringCellValue, out d))
                            return isExpress
                            ? Activator.CreateInstance(propType, d)
                            : d;
                        break;
                    default:
                        Log.WriteLine("There is no suitable value for {0} in cell {1}{2}, sheet {3}", propType.Name, CellReference.ConvertNumToColString(cell.ColumnIndex), cell.RowIndex + 1, cell.Sheet.SheetName);
                        break;
                }
                return null;
            }

            if (type == typeof(int) || type == typeof(long))
            {
                switch (cell.CellType)
                {
                    case CellType.Numeric:
                    case CellType.String:
                        var l = type == typeof(int) ? Convert.ToInt32(cell.NumericCellValue) : Convert.ToInt64(cell.NumericCellValue);
                        return isExpress ? Activator.CreateInstance(propType, l) : l;
                    default:
                        Log.WriteLine("There is no suitable value for {0} in cell {1}{2}, sheet {3}", propType.Name, CellReference.ConvertNumToColString(cell.ColumnIndex), cell.RowIndex + 1, cell.Sheet.SheetName);
                        break;
                }
                return null;
            }

            if (type == typeof(bool))
            {
                switch (cell.CellType)
                {
                    case CellType.Numeric:
                        var b = (int)cell.NumericCellValue != 0;
                        return isExpress ? Activator.CreateInstance(propType, b) : b;
                    case CellType.String:
                        bool i;
                        if (bool.TryParse(cell.StringCellValue, out i))
                            return isExpress ? Activator.CreateInstance(propType, i) : i;
                            Log.WriteLine("Wrong boolean format of {0} in cell {1}{2}, sheet {3}", cell.StringCellValue, CellReference.ConvertNumToColString(cell.ColumnIndex), cell.RowIndex + 1, cell.Sheet.SheetName);
                        break;
                    case CellType.Boolean:
                            return isExpress ? Activator.CreateInstance(propType, cell.BooleanCellValue) : cell.BooleanCellValue;
                    default:
                            Log.WriteLine("There is no suitable value for {0} in cell {1}{2}, sheet {3}", propType.Name, CellReference.ConvertNumToColString(cell.ColumnIndex), cell.RowIndex + 1, cell.Sheet.SheetName);
                        break;
                }
                return null;
            }

            //enumeration
            if (type.IsEnum)
            {
                if (cell.CellType != CellType.String)
                {
                    Log.WriteLine("There is no suitable value for {0} in cell {1}{2}, sheet {3}", propType.Name, CellReference.ConvertNumToColString(cell.ColumnIndex), cell.RowIndex + 1, cell.Sheet.SheetName);
                    return null;
                }
                try
                {
                    var eValue = cell.StringCellValue;
                    var eMember = GetAliasEnumName(type, eValue);
                    //if there was no alias try to parse the value
                    var val = Enum.Parse(type, eMember ?? eValue, true);
                    return val;
                }
                catch (Exception)
                {
                    Log.WriteLine("There is no suitable value for {0} in cell {1}{2}, sheet {3}", propType.Name, CellReference.ConvertNumToColString(cell.ColumnIndex), cell.RowIndex + 1, cell.Sheet.SheetName);
                }
            }

            //if not suitable type was found, report it 
            throw new Exception("Unsupported type " + type.Name + " for value '" + cell + "'");
        }

        private void SetSimpleValue(PropertyInfo info, object obj, ICell cell, object index = null)
        {
            //check there is a setter defined for this property
            if (info.GetSetMethod() == null)
            {
                if (info.DeclaringType != null)
                    Log.WriteLine("There is no setter for {0} in {1}", info.Name, info.DeclaringType.Name);
                return;
            }

            //return if there is no value in she cell
            if (cell.CellType == CellType.Blank) return;

            var type = info.PropertyType;
            var value = CreateSimpleValue(type, cell);
            info.SetValue(obj, value, index == null ? null : new []{index});
        }

        private string GetAliasEnumName(Type type, string alias)
        {
            string result;
            return _aliasesEnumCache.TryGetValue(type.Name + "." + alias, out result) ? result : null;
        }

        #endregion
    }
}