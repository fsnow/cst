# Vector Search & RAG in CST: A Feasibility Study

This document provides a strategic overview and a set of recommendations for integrating vector search and Retrieval-Augmented Generation (RAG) capabilities into the CST application. The primary constraint guiding this strategy is that the feature must be **completely free** for both the developer to host and for the end-user to operate.

## 1. Executive Summary: The Local-First Approach

The only viable way to meet the "free" constraint is to perform all AI processing **on the user's local machine**. This approach avoids all API costs and server infrastructure expenses. The strategy involves using relatively small, efficient, open-source models for creating vector embeddings and, optionally, for generating text.

The recommended implementation is a two-phase process:
1.  **Phase 1: Implement Semantic Search.** This is the core of the feature. It involves pre-calculating vector embeddings for the Pāli texts and bundling them with the application. The user can then search using natural language questions, and the application will return the most relevant paragraphs from the Tipiṭaka. This phase **does not require a full Large Language Model (LLM)** for text generation and is achievable on most modern computers.
2.  **Phase 2: Optional Local LLM for RAG.** For users with more powerful hardware (e.g., a modern GPU), the application can offer an optional download of a small, quantized LLM. This would enable a true chat feature, where the semantically retrieved paragraphs are fed to the local LLM to synthesize a natural language answer.

---

## 2. The Pāli Language & Script Problem

### Which LLMs Understand Pāli?
Pāli is a low-resource language. No major commercial or open-source model is explicitly trained on it. However, large multilingual models have been exposed to Pāli through their vast training data, which includes parts of the internet and academic archives.

-   **Commercial Models (Not an option due to cost)**: Google's Gemini and Anthropic's Claude models have shown some of the strongest capabilities with ancient and low-resource languages.
-   **Open-Source Models (The Recommended Path)**: Models like Mistral, Llama, and especially multilingual sentence transformers have encountered Pāli. The key is not to find a model that *specializes* in Pāli, but one that has a good multilingual representation space.

### Which Script is Best for Embeddings?
This is a critical decision. The choice of script directly impacts the quality of the vector embeddings.

**Recommendation: Use Romanized (Latin) Pāli exclusively.**

**Reasoning:**
1.  **Maximum Exposure**: The vast majority of Pāli text available on the web and in academic databases is in a Romanized format. The models have seen and learned Pāli words and grammar primarily through this script.
2.  **Reduces Ambiguity**: Using a single, standardized script prevents the model from getting confused. For an LLM, "သော" (Burmese), "सो" (Devanagari), and "so" (Latin) are three visually distinct tokens. Forcing them all to the single token "so" concentrates the model's understanding of that word into a single vector representation, leading to much higher quality results.
3.  **Simplifies the Pipeline**: The application already has a robust script conversion engine. By converting all text to the Latin script before chunking and embedding, you create a single, clean, and consistent dataset for the AI model to process.

---

## 3. Chunking Strategy for Pāli Texts

The goal of chunking is to create self-contained blocks of text that are semantically meaningful.

**Recommendation: Use the paragraph as the primary chunking unit.**

**Reasoning:**
1.  **Semantic Cohesion**: In the Tipiṭaka, a paragraph (`<p>` tag in the XML) is almost always a complete thought or a significant part of a narrative or argument. This makes it an ideal unit for semantic retrieval.
2.  **Practicality**: The existing XML structure with `<p>` tags makes this trivial to implement. You can parse the XML and treat the text content of each `<p>` tag as a chunk.
3.  **Metadata is Crucial**: For each chunk, you must store its origin as metadata. This includes:
    -   Book Name / Abbreviation
    -   XML Filename
    -   Chapter / Section
    -   Paragraph Number
    -   Page numbers for all editions (VRI, PTS, Myanmar, Thai)
    This metadata is essential for providing citations with the search results, allowing the user to navigate directly to the source.

**Should you use multiple chunking strategies?**
For this use case, **no**. A multi-strategy approach (e.g., embedding words, sentences, and paragraphs) adds significant complexity, increases the size of the vector database, and provides diminishing returns. Focusing on high-quality paragraph chunks with rich metadata is the most effective path.

---

## 4. Recommended Implementation Strategy

This is a "think outside the box" plan that delivers a powerful feature for free.

### **Phase 1: Semantic Search (The Core Feature)**

This phase is performed **by the developer** before shipping the application.

1.  **Data Preparation**:
    -   Write a one-time script that iterates through all 217 XML files.
    -   For each file, convert the Pāli text to **Romanized (Latin) script**.
    -   Parse the Romanized XML. For each `<p>` tag, extract its text content and all relevant metadata (book, chapter, page numbers). This is your "chunk".

2.  **Embedding Generation**:
    -   Choose a high-quality, open-source, multilingual sentence-transformer model. A great choice is `paraphrase-multilingual-MiniLM-L12-v2` from the Sentence-Transformers library. It's small, fast, and works well across many languages.
    -   Use this model to convert each text chunk into a vector embedding (a list of numbers).

3.  **Create a Local Vector Database**:
    -   Store all the vector embeddings and their associated metadata in a local vector database file.
    -   **Recommendation**: Use **FAISS (Facebook AI Similarity Search)**. It is an incredibly fast and memory-efficient library, not a server. You can create a single FAISS index file and bundle it directly with your application. It has no runtime dependencies beyond the library itself.

4.  **Application Integration**:
    -   Bundle the FAISS index file and the sentence-transformer model with the CST application.
    -   When the user types a search query, the application will:
        a. Use the local sentence-transformer model to create an embedding of the user's query.
        b. Use the FAISS library to search the bundled index file for the "k" most similar vectors.
        c. Retrieve the metadata for these top "k" results.
        d. Display the corresponding text chunks to the user, beautifully formatted with their citations.

**This completes Phase 1. You have now delivered a powerful, free, offline-first semantic search feature.**

### **Phase 2: Optional RAG with a Local LLM (Advanced)**

This phase is for power users and requires them to take an extra step.

1.  **Integrate a Local LLM Runner**:
    -   Add a library like `llama.cpp` (or a .NET wrapper for it) to the application. This library can run quantized LLMs efficiently on a CPU or GPU.
2.  **Offer Optional Model Downloads**:
    -   In the application's settings, provide a link or a simple interface for users to download a small, quantized, open-source chat model (e.g., a 3-Billion parameter model from the Mistral or Llama family in GGUF format). You would not host this file; you would link to its source on a platform like Hugging Face.
3.  **Implement the RAG Workflow**:
    -   When a user with a local LLM enabled performs a search, the application follows the same steps as in Phase 1.
    -   However, instead of just displaying the results, it will:
        a. Take the top 3-5 retrieved text chunks.
        b. Format them into a single text block as "context".
        c. Create a prompt that looks something like this:
           ```
           Context:
           [Text of chunk 1]
           [Text of chunk 2]
           [Text of chunk 3]

           Based on the context provided above, please answer the following question:
           Question: [User's original query]
           Answer:
           ```
        d. Feed this prompt to the local LLM via the `llama.cpp` interface.
        e. Stream the LLM's generated answer back to the user in a chat interface.

By making the most resource-intensive part (the generative LLM) optional and user-provided, you maintain the "free" constraint while still offering state-of-the-art capabilities to those who can support it.
