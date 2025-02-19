//-----------------------------------------------------------------------
// <copyright file="ILoaderWithInclude.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Raven.Client.Documents.Session.Loaders
{
    /// <summary>
    /// Fluent interface for specifying include paths
    /// for loading documents
    /// </summary>
    public interface ILoaderWithInclude<T>
    {
        /// <inheritdoc cref="IDocumentIncludeBuilder{T,TBuilder}.IncludeDocuments(string)"/>
        ILoaderWithInclude<T> Include(string path);

        /// <inheritdoc cref="IDocumentIncludeBuilder{T,TBuilder}.IncludeDocuments(Expression{Func{T, string}})"/>
        ILoaderWithInclude<T> Include(Expression<Func<T, string>> path);

        /// <inheritdoc cref="IDocumentIncludeBuilder{T,TBuilder}.IncludeDocuments{TInclude}(Expression{Func{T, string}})"/>
        ILoaderWithInclude<T> Include<TInclude>(Expression<Func<T, string>> path);

        /// <inheritdoc cref="IDocumentIncludeBuilder{T,TBuilder}.IncludeDocuments(Expression{Func{T, IEnumerable{string}}})"/>
        ILoaderWithInclude<T> Include(Expression<Func<T, IEnumerable<string>>> path);

        /// <inheritdoc cref="IDocumentIncludeBuilder{T,TBuilder}.IncludeDocuments{TInclude}(Expression{Func{T, IEnumerable{string}}})"/>
        ILoaderWithInclude<T> Include<TInclude>(Expression<Func<T, IEnumerable<string>>> path);

        /// <summary>
        /// Loads the specified ids.
        /// </summary>
        /// <param name="ids">The ids.</param>
        /// <returns></returns>
        Dictionary<string, T> Load(params string[] ids);

        /// <summary>
        /// Loads the specified ids.
        /// </summary>
        /// <param name="ids">The ids.</param>
        /// <returns></returns>
        Dictionary<string, T> Load(IEnumerable<string> ids);

        /// <summary>
        /// Loads the specified id.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns></returns>
        T Load(string id);

        /// <summary>
        /// Loads the specified ids.
        /// </summary>
        /// <param name="ids">The ids.</param>
        /// <returns></returns>
        Dictionary<string, TResult> Load<TResult>(params string[] ids);

        /// <summary>
        /// Loads the specified ids.
        /// </summary>
        /// <param name="ids">The ids.</param>
        /// <returns></returns>
        Dictionary<string, TResult> Load<TResult>(IEnumerable<string> ids);

        /// <summary>
        /// Loads the specified id.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="id">The id.</param>
        /// <returns></returns>
        TResult Load<TResult>(string id);
   }
}
