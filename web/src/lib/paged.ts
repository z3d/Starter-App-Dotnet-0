import { useInfiniteQuery } from '@tanstack/react-query'
import { useMemo } from 'react'

export interface PagedResponse<T> {
  data: T[]
  hasMore: boolean
}

export interface PagedList<T> {
  data: T[] | undefined
  isPending: boolean
  isFetched: boolean
  error: Error | null
  hasMore: boolean
  loadingMore: boolean
  loadMore: () => void
}

export function usePaged<T>(options: {
  queryKey: readonly unknown[]
  queryFn: (page: number) => Promise<PagedResponse<T>>
  enabled?: boolean
}): PagedList<T> {
  const query = useInfiniteQuery({
    queryKey: options.queryKey,
    queryFn: ({ pageParam }) => options.queryFn(pageParam),
    initialPageParam: 1,
    getNextPageParam: (last, all) => (last.hasMore ? all.length + 1 : undefined),
    enabled: options.enabled,
  })
  const pages = query.data?.pages
  const data = useMemo(() => pages?.flatMap((page) => page.data), [pages])
  return {
    data,
    isPending: query.isPending,
    isFetched: query.isFetched,
    error: query.error,
    hasMore: query.hasNextPage,
    loadingMore: query.isFetchingNextPage,
    loadMore: () => void query.fetchNextPage(),
  }
}

export function countLoaded(count: number, more: boolean) {
  return more ? `${count}+` : String(count)
}
